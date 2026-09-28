# TASK 03 — BTCX receiving payment implementation plan

Status: planning only. No blockchain payment flow is implemented. Do not treat this document as approval to deploy or use mainnet.

## Baseline and design constraints

- BTCPay Server is fixed at `v2.4.4`, submodule commit `2d5a0d8077bb33af080e949031da33d84b80638d`.
- XBoard is fixed at `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`.
- bitcoin-pocx integration repository: `005bf0098e217b76a2627bfae458dff4f5718dd5`; its Bitcoin source submodule: `b88b852644f629cd5f25b3424d11b462462c24b3`.
- BTCX Rust wallet repository: `6907bacb132324e460cbe55d3765b4350bb56e61`.
- Phoenix PoCX: release `v2.4.0`, commit `bc4713306c9c2cd3cbf989a3e355e0705b485218`.
- Existing plugin is a skeleton and cannot receive or settle a BTCX payment. Do not edit BTCPay core or XBoard.
- Order denomination stays CNY. Manual rate `1 BTCX = X CNY` is snapshotted per invoice; rate changes apply only to new invoices. Payment values use integer atomic units (10^8 per BTCX); no binary floating-point for money.
- PoCX block headers are 286 bytes. Generic Bitcoin header/block parsers that assume 80-byte headers cannot be used for PoCX blocks.

## Ordered implementation stages and gates

### 03.0 — Repair prompt snapshot serialization (prerequisite) — implemented

Runtime smoke found that serialized integer `rateTimestamp` can make `BtcxPaymentMethodHandler.ParsePaymentPromptDetails` fail with a Newtonsoft `JsonReaderException` (`Unexpected token Integer`, path `rateTimestamp`). A BTCX property converter now accepts supported numeric time units and writes canonical UTC ISO-8601 text, with unit round-trip tests that preserve the quote fields. The live Greenfield retrieval gate (`includePaymentMethods`) remains for the isolated runtime acceptance run; see [task-03-0-prompt-serialization.md](task-03-0-prompt-serialization.md).

### 03.1 — BTCX network identity and amount primitives

Represent BTCX mainnet/testnet/regtest explicitly, including genesis identity, magic, address parameters, ports, 8 decimal places, and atomic-unit conversion. Keep dust as relay policy, not a fixed consensus constant. Do not register BTCX as Bitcoin's `BTCPayNetwork`/NBXplorer network unless an audited PoCX-aware implementation is supplied. Unit-test network selection, amount bounds, formatting and integer conversion.

### 03.2 — Address decoding and script construction

Use BTCX-specific HRP/Base58 versions. Implement/test address → scriptPubKey for P2PKH, P2SH and supported witness programs. Compare vectors with the pinned Rust `ChainParams.parse_address`; reject bad checksum, unsupported witness version, wrong network and malformed payload. No address string matching for payments: match exact output script bytes.

### 03.3 — Read-only PoCX node RPC adapter

Pin the node executable/source and implement a narrow authenticated client for chain identity, tip, raw transaction/block lookup and mempool/canonical confirmation checks. RPC stays on loopback/private network and uses cookie/auth; never log credentials. Fake-HTTP tests cover errors, timeouts, malformed data, wrong genesis, restart and unavailable node. Confirm exact RPC availability against the pinned PoCX build before coding.

### 03.4 — Receiving address allocation and persistence

Prototype quickest on isolated regtest using a dedicated PoCX node wallet RPC, with receive-only operational controls and no withdrawal UI/API. Before production, assess watch-only descriptor/xpub allocation or an isolated address service; define wallet backup, restore and address-index durability. Reserve one unique address per invoice before invoice save and persist its exact script/tracked token with the invoice. Test uniqueness, concurrency, retry/idempotency and restart. No private key should enter plugin logs or invoice records.

### 03.5 — Prompt, amount, URI and checkout

Extend `BtcxPaymentMethodHandler` to produce a persisted destination/script tracking token and immutable rate/amount snapshot. Implement payment link and checkout extension using Phoenix-compatible canonical URI `btcx:<address>?amount=<fixed-decimal>`; format invariant decimal without exponent, at most 8 places. Fix prompt serializer first. Test Greenfield retrieval, QR decode, precision and Phoenix parser vectors. Payment method is unavailable if no valid rate or address can be allocated.

### 03.6 — Discovery adapter

Evaluate pinned `electrs-btcx` + `bindex-btcx` on regtest for script-hash history/mempool notifications. Treat indexer as a discovery/wakeup source only. Confirm its 286-byte PoCX parser and REST/node version compatibility. Keep direct node RPC reconciliation as chain authority. Do not use `esplora-pocx` frontend repo as if it were the Esplora backend; pin/audit `esplora-electrs-pocx` separately before considering it.

### 03.7 — Payment persistence and idempotency

Implement an `IHostedService` listener/reconciler, not a nonexistent generic payment-listener interface. Resolve invoice by stored script/tracked destination, validate transaction output, create `PaymentData` in atomic units and call BTCPay `PaymentService.AddPayment`. Use stable identity `(network, txid, vout)` and BTCPay payment uniqueness; publish `InvoiceEvent.ReceivedPayment` after a new payment is accepted. Test duplicate notifications, multi-output, multiple transactions and exact invoice mapping.

### 03.8 — Confirmations, expiry and state mapping

Recompute confirmations against canonical PoCX tip/block hash. BTCPay `PaymentStatus` is `Processing`, `Settled`, or `Unaccounted`; there is no separate confirmed enum. Move payment to `Settled` only at configured BTCX confirmation policy; use `Unaccounted` when a previously seen output disappears/is replaced. Update with `PaymentService.UpdatePayments`, publish invoice update event and let `InvoiceWatcher` aggregate invoice state. Test pending, threshold, underpay, overpay, expired, late and stale confirmations.

### 03.9 — Reorg/replacement/restart reconciliation

Persist enough cursor/scan state or rebuild from monitored invoice scripts with a bounded overlap. On every startup, node/indexer recovery or reorg, reconcile all monitored invoice destinations and recent canonical blocks; do not rely on transient notifications. Reverse stale confirmations/payment accounting after reorg or RBF. Test node/plugin/indexer restarts, dropped mempool tx, replacement, block disconnect, missing tx and reorg across threshold.

### 03.10 — Regtest and Phoenix end-to-end receiving proof

Run isolated PoCX regtest; allocate invoice address, generate BTCPay checkout URI/QR, parse/import with pinned Phoenix, send transaction, observe mempool, mine blocks, and verify payment/invoice state and webhook. Test mainnet/testnet/regtest rejection boundaries and Phoenix display of 37.5, 0.1 and 0.00000001 BTCX. No production credentials or mainnet payment.

### Later XBoard fulfillment gate

XBoard webhook hardening and end-to-end fulfillment are a separate gate: accept only the intended event, verify raw-body HMAC, retrieve invoice server-to-server, compare store/invoice/order metadata/amount/currency/payment method/state/expiry, deduplicate, and fulfill only policy-approved settled invoices. Existing XBoard provider does not establish these checks. Do not silently count a mempool sighting as fulfillment.

## Definition of done for receiving MVP

Regtest can repeatedly create a CNY invoice with immutable BTCX amount/rate, produce a Phoenix-readable URI and unique BTCX address, observe exact output, survive process restarts and duplicate notifications, settle only at the configured canonical confirmation threshold, recover from reorg/RBF, and issue a valid BTCPay webhook. All values are exact integer atomic units; no key material or RPC secret is exposed. Production readiness additionally requires wallet recovery/security review, pinned indexer/backend, monitoring and XBoard webhook hardening.

## Test matrix

| Stage | Unit tests | Integration/regtest | Failure tests |
|---|---|---|---|
| 03.0/03.5 prompt | JSON round-trip for integer/string timestamp, snapshot immutability, URI fixed formatting | Greenfield create/retrieve including payment methods, QR decode | Legacy/malformed details, missing rate/address |
| 03.1/03.2 network/script | network vectors, checksums, witness versions, script vectors, amount boundaries | compare scripts/output to PoCX node and Rust vectors | wrong network, invalid checksum, unsupported script |
| 03.3/03.4 RPC/address | RPC DTO/auth, address uniqueness and reservation idempotency | dedicated regtest wallet and restart | timeout, auth reject, wallet locked/unavailable, duplicate allocation |
| 03.6/03.7 detection | output matching, `(network,txid,vout)` idempotency, multi-output sum | mempool transaction to monitored script | duplicate events, index lag, missing tx, foreign output |
| 03.8/03.9 state | confirmation math, expiry, RBF/reorg transitions | mine, disconnect/reorg, restart node/plugin/indexer | stale tip, tx replacement, scan gap, indexer outage |
| 03.10 Phoenix | parser vectors, exact 8dp and URI encoding | Phoenix receive/import and regtest send | wrong scheme/network, malformed amount/QR |

## Failure policy summary

| Failure | Expected behavior | Owner | Recovery |
|---|---|---|---|
| Underpayment | remain New/Processing; no fulfillment | listener + InvoiceWatcher | account each output; await balance or expiry |
| Overpayment | settle invoice once due is covered; excess is auditable, never duplicate fulfillment | BTCPay aggregation + later refund policy | manual/refund workflow; XBoard idempotency |
| Duplicate payment event | no duplicate value | listener + PaymentService | unique outpoint key and reconciliation |
| Multiple outputs/payments | validate and account each matching output independently | listener | sum atomic units by invoice; retain tx/vout identity |
| Late payment | preserve expired/paid-late semantics; no automatic delivery | InvoiceWatcher/XBoard policy | operator review or explicit product rule |
| Invoice expiry | stop accepting normal checkout; monitor according to BTCPay lifecycle | InvoiceWatcher | chain events still reconcile; classify late payment |
| RBF/replacement | old output no longer counted if absent; replacement counted once | listener/reconciler | canonical mempool/chain reconciliation |
| Reorg | lower confirmation count; revoke stale settlement accounting | listener + PaymentService | rescan canonical blocks; XBoard fulfillment needs compensation policy |
| Node/plugin restart | no lost payments/state | BTCX hosted service | startup reconciliation from persisted monitored scripts/cursor |
| Indexer restart/outage | discovery delayed; never promote based on stale observation | discovery adapter | retry and node-backed rescan |
| Missing tx | retain pending/reconcile, don't infer settlement | reconciliation service | query canonical node, alert on persistent disagreement |
| Stale confirmation | recompute at current canonical tip | listener | refresh block hash/height and payment state |
| Wallet unavailable | BTCX option/address allocation fails closed; BTCPay stays up | address provider/plugin | retry; existing invoices remain monitored from saved scripts |
