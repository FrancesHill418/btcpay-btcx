# TASK 03.5 — BTCX Confirmations and Reorg Recovery

Status: implemented in the payment listener reconciliation path. Confirmation behavior is source-verified and uses BTCPay v2.4.4 invoice speed policy.

## Chain semantics

Pinned `bitcoin-pocx` commit `005bf0098e217b76a2627bfae458dff4f5718dd5` retains Core's `getrawtransaction(txid, verbosity, blockhash?)` behavior in `bitcoin/src/rpc/rawtransaction.cpp`:

- Without `blockhash`, lookup finds mempool transactions by default and confirmed transactions when `-txindex` is enabled. Verbosity 1 returns decoded `vin`/`vout`, transaction block metadata and confirmations.
- With a block hash, lookup is constrained to that block and explicitly returns `in_active_chain`. A transaction in a stale block can still be read, but `in_active_chain` is false and its confirmation depth is non-positive.
- `getblockcount` returns the active chain's height (genesis height 0); `getblockhash(height)` identifies the current active block at that height.
- `vin[].sequence < 0xfffffffe` signals opt-in replacement under the upstream transaction policy and feeds BTCPay's existing HighSpeed rule.

BTCX PoCX adds its proof/plot fields to block/header serialization but does not change transaction output identity, transaction confirmations, or active-chain lookup semantics. The pinned PoCX changes do not introduce deterministic finality. Therefore the plugin does not claim an irreversible/final state: BTCPay `Settled` reflects the configured confirmation policy and is reversible on reorg.

## State mapping

Each scan rebuilds the current outpoint set from electrs-btcx script history, then verifies positive-height transactions against PoCX's current block hash and decoded transaction. A node/indexer race (RPC code `-5`, stale block mismatch, or non-active block) makes that scan incomplete, so it does not demote payments based on partial observations. A complete successful scan applies the current observations to persisted BTCX payments:

- Current mempool output: `Processing`, 0 confirmations (or `Settled` only when BTCPay's invoice policy requires zero).
- Canonical chain output: `Processing` until required confirmations; then `Settled`.
- Previously recorded output absent from a complete current script history: `Unaccounted`.
- Reorged output still in mempool: it reappears as `Processing`; if later re-mined it returns to the applicable confirmation policy and may settle again.
- Reorged/dropped/replaced output no longer present: `Unaccounted`.
- RPC/indexer unavailable or a partial indexer/node race: preserve the last stored state and retry next poll.

Required confirmations match BTCPay v2.4.4 `NBXplorerListener.ConfirmationRequired`: HighSpeed 0 (1 when the transaction signals RBF), MediumSpeed 1, LowMediumSpeed 2 and LowSpeed 6. The plugin reads the invoice's existing `SpeedPolicy`; this avoids inventing a BTCX-only threshold and keeps checkout/invoice aggregation semantics aligned with the installed BTCPay version. This is a merchant risk policy, not a PoCX consensus finality guarantee.

State and observed block details are saved in BTCPay `PaymentService.UpdatePayments`. Settlement transitions use its standard `InvoiceEvent.PaymentSettled`; the plugin publishes `InvoiceNeedUpdateEvent` after changed payment states so `InvoiceWatcher` recomputes partial, exact, overpaid, expired and late invoice status. Invoice expiry does not itself make a payment settled or authorize XBoard fulfillment.

## Restart and reorg behavior

The listener rehydrates invoice/address bindings from BTCPay's persisted `AddressInvoices` table on startup. Each cycle retrieves full Electrum history for every tracked BTCX script and reconciles existing output IDs as well as newly observed outputs. No transient notification or in-memory checkpoint is required for correctness. Historical invoices remain monitored to detect late payments and reversals.

The electrs/bindex cache handles chain reorgs at the index layer; the PoCX node remains authoritative for active block membership. During temporary index lag the plugin leaves prior payment states intact. After the index converges, it either observes the transaction in mempool/new canonical block and updates it or marks a disappeared output Unaccounted. No withdrawal, rebroadcast or transaction replacement is attempted.

## Tests and remaining environment validation

Unit coverage verifies canonical block binding, stale block rejection, active-chain checks, mempool state, RBF sequence detection, policy thresholds, duplicate polling, and preservation through node outages. Full persistence/InvoiceWatcher settlement and an actual mined-block reorg still require the isolated BTCX regtest + development BTCPay/PostgreSQL runtime environment; no production service or wallet was contacted.
