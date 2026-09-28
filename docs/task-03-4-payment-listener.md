# TASK 03.4 — BTCX Payment Listener / Discovery

Status: implemented with a read-only Electrum history client and BTCPay payment sink. Verification is mock-only; no indexer, node, or wallet was contacted.

## Discovery decision

Use the pinned `electrs-btcx` Electrum server as the sole first-version address discovery source. It uses the pinned `bindex-btcx` chain index internally:

- electrs-btcx commit `2f78c63e20215e20944767f0901209c4d740fe5b` implements `blockchain.scripthash.get_history` and returns `{ tx_hash, height }` history rows. Positive heights are indexed chain transactions; height `0` is unconfirmed, and `-1` is unconfirmed with unconfirmed inputs. Its tracker includes current mempool transactions unless configured with `--ignore-mempool`; the default configuration example reports `ignore_mempool: false`.
- electrs delegates block indexing to bindex-btcx commit `eda7c70660baa06affef464c7ea1e131c39304f1`. Its parser explicitly reads 286-byte PoCX headers and its cache removes stale branch data when a reorg is detected. It requires bitcoin-pocx Core v31+ with `-rest` enabled.
- Electrum script hashes are SHA256(scriptPubKey) in reversed byte order. `BtcxElectrumScriptHash` follows the pinned bindex `ScriptHash` implementation.

This keeps script-history discovery out of a plugin-wide full chain scan while delegating PoCX block parsing to BTCX-aware upstream code. Direct RPC alone has no efficient address-history query. `esplora-pocx` is a frontend/API documentation repository; its `esplora-electrs-pocx` backend revision is not pinned here. Running multiple discovery implementations would add conflicting mempool/reorg behavior without increasing canonical trust.

Electrs is designed for personal/small-scale deployments and its own README warns that a public multi-user service is resource intensive. Keep it private and monitor resource consumption. For this initial development version the listener polls each persisted BTCX invoice address, including expired/settled invoices, to preserve late-payment and restart/reorg visibility. That is intentionally complete for correctness but scales with the total number of BTCX invoices; a production-scale deployment needs persistent Electrum subscriptions or a separately capacity-tested indexed subscriber.

## Trust and processing flow

```text
persisted AddressInvoices + active BTCPay prompts
  → Electrum scripthash.get_history (discovery hint only)
    → PoCX getblockhash(height) for confirmed history
    → PoCX getrawtransaction(txid, 1, blockhash?)
      → verify canonical block facts and exact output scriptPubKey bytes
        → create one BTCPay PaymentData per matching vout
          → PaymentService.AddPayment + invoice events
```

For a mempool row, the listener fetches the tx without a block hash and records it as `Processing` with 0 confirmations. For a positive history height, it gets the node's current `getblockhash(height)`, looks the tx up with that block hash, and requires `in_active_chain`, a matching returned block hash and positive node-reported confirmations. If electrs is ahead/behind the node or the transaction disappears during a mempool/block transition (`-5`), the scan skips that observation and retries next cycle. It never credits an indexer-supplied amount or height without parsing the node-decoded transaction.

The decoded transaction is matched against the saved invoice scriptPubKey by exact bytes. Each positive, representable output is independently accounted; payment identity is `{network}-{txid}-{vout}`. This supports multiple outputs in one transaction, split payments across transactions, and duplicate electrs rows/polls. BTCPay's persisted `(PaymentData.Id, PaymentMethodId)` uniqueness is the restart/idempotency boundary. The sink calls `PaymentService.AddPayment`, then publishes `InvoiceEvent.ReceivedPayment` and `InvoiceNeedUpdateEvent` only for a newly persisted payment.

All valid matching outputs first enter BTCPay as `Processing`. Existing `InvoiceWatcher` aggregation owns invoice amounts, partial/overpayment and expiry state. This listener does not settle any payment: canonical confirmation thresholds, disappeared mempool payments, and reorg reversal are the next confirmation/reorg stage. An expired invoice may still receive a recorded late payment; BTCPay's invoice lifecycle decides the expired/late state, and XBoard fulfillment must separately reject an expired invoice unless an explicit reviewed policy allows it.

## Restart recovery and configuration

The listener loads persisted BTCX `AddressInvoices` from BTCPay's supported repository database context on startup in bounded invoice batches, then merges `InvoiceRepository.GetMonitoredInvoices` each cycle for newly activated invoices. It retains historical destination scripts so late payments and reorgs remain discoverable after restart. No listener-specific checkpoint database is introduced. Missing/malformed snapshots fail closed.

Configuration is disabled by default:

```json
{
  "BTCX": {
    "Electrum": {
      "Enabled": true,
      "Endpoint": "tcp://127.0.0.1:50001/",
      "PollIntervalSeconds": 15,
      "TimeoutSeconds": 10,
      "MaxRetries": 2
    },
    "Wallet": {
      "Network": "regtest"
    }
  }
}
```

Use an internal Docker network endpoint for Electrs. The client rejects public IPs and filters DNS results to loopback/private/link-local addresses at connect time. Electrum has no authentication in this integration; do not publish its port. For mempool visibility, ensure Electrs is not started with `--ignore-mempool`. The wallet network option is restricted to testnet/regtest in this development build; the listener refuses mainnet.

## Tests and runtime evidence

Mock Electrum TCP tests cover the JSON-RPC method/script hash, history schema, malformed response, cancellation, unavailable index and public endpoint rejection. Reconciler tests cover mempool and confirmed observations, exact/under/over amounts, multiple outputs, split payment, duplicate history/poll/restart, stale/noncanonical and missing transactions, and node outage. The gated PostgreSQL BTCPay runtime smoke now also starts pinned PoCX regtest, allocates a real wallet address, sends an exact 37.5 BTCX development transaction, presents its txid through an Electrum protocol fixture, verifies payment persistence/idempotency and six-confirmation settlement, invalidates the containing regtest block to move the payment back to Processing, then reconsiders the block and verifies resettlement. A real electrs-btcx startup was attempted against the current PoCX regtest node. It exits before indexing because its pinned bindex dependency requests `/rest/blockpart/<genesis>.bin?offset=0&size=491` and receives HTTP 404; bindex reports `use https://github.com/bitcoin/bitcoin/pull/33657`. Therefore the transaction lifecycle is runtime-tested but real address indexing is blocked. Full exact output and compatibility details are in [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).
