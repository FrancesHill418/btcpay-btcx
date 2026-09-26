# BTCX invoice and payment flow

This is a proposed state/data flow for the plugin. BTCPay contract names must be rechecked against the pinned checkout before code is written.

## Create invoice

1. XBoard creates its order and stable order UUID, then calls BTCPay's selected invoice API with fiat amount/currency and order metadata.
2. BTCPay locks its rate and invoice expiry under the existing invoice model. The BTCX payment handler receives a prepared payment amount in integer smallest units and records the exact rate/source/timestamp/expiry needed for audit. BTCX amount must never be recomputed after invoice creation.
3. The plugin reserves a unique BTCX receiving script/address for the invoice from a receive-only derivation path. Persist invoice ID ↔ script ↔ network ↔ derivation index atomically before presenting it.
4. BTCPay's checkout shows BTCX amount, address, URI/QR and expiry. XBoard uses the returned checkout URL; it does not receive node/indexer credentials or monitor the chain.

## Observe and account for transactions

1. A BTCX-aware listener subscribes/polls for the invoice script's activity through the chosen indexer and persists a durable scan cursor. Node RPC supplies canonical chain facts.
2. Parse each relevant output and compare its script bytes to the reserved invoice script. Convert value to exact integer base units; reject floats and out-of-range/negative values.
3. Insert one observed payment per `(network, txid, vout)` under a unique database constraint. Repeated notification, poll, webhook, or restart must become an idempotent upsert/reconciliation, not a second payment.
4. Mempool outputs are provisional. Aggregate distinct received outpoints for partial/exact/overpayment evaluation. Replaced, evicted, conflicting, or spent observations are rechecked with node/indexer state.
5. For confirmed outputs, record block hash/height and confirmation count from the current best chain. On a block disconnect or a changed canonical block at the stored height, reverse affected observations to unconfirmed/orphaned and recompute the invoice state. Never make a one-way `confirmed` transition.
6. Feed valid payment updates through BTCPay's invoice/payment handler so its invoice state machine, UI and webhook delivery remain authoritative. Do not write BTCPay core tables directly.

## State behavior to specify and test

| Situation | Payment interpretation | Invoice action |
|---|---|---|
| Exact amount in mempool | Received but zero confirmations | Await configured confirmation policy; optionally expose processing status |
| Exact amount confirmed to threshold | Distinct output total reaches due | Complete/settle via current BTCPay payment contracts |
| Underpayment or split payments | Sum unique valid outputs; preserve each payment separately | Keep due while unexpired; complete only once aggregate meets policy |
| Overpayment | Preserve full received total; never truncate/overwrite | Follow BTCPay's existing overpayment semantics; surface excess in status/details |
| Duplicate indexer notification or poll | Same outpoint | No duplicate payment or webhook side effect |
| Expired invoice, no payment | No eligible payment before expiry | Expired |
| Payment after expiry | Record late payment with timestamp/chain facts | Apply BTCPay's current late-payment/invalid behavior; never silently fulfill |
| Reorg removes inclusion block | Payment becomes unconfirmed/orphaned | Recompute invoice and revoke settled/confirmed status when BTCPay contract allows |
| Node/indexer/plugin restart | Replay from durable cursor and reconcile last safe range | Same result as uninterrupted processing |

Exact status labels and transition permissions depend on current BTCPay payment architecture and must be verified in the restored source. Do not invent state names or directly manipulate invoice status.

## Webhook to XBoard

BTCPay sends invoice events; XBoard validates the configured authentication/signature, binds the invoice ID to its stored order UUID, validates event type/status/currency/amount against the invoice retrieval response or trusted event data, and records delivery/event identity before fulfillment. A repeated delivery is acknowledged without repeating entitlement activation. Use a terminal, policy-approved invoice event for fulfillment; `InvoiceProcessing` may mean full amount observed but not sufficiently confirmed. Reorg behavior after an order is fulfilled requires an explicit merchant policy and compensating/manual-review path.

Prefer Greenfield's signed webhook contract if XBoard can use it. BTCPay documentation separates Greenfield webhooks from legacy BitPay notifications; do not assume equivalent payloads or authentication semantics. If XBoard requires legacy BitPay-only, test invoice creation, payment-method selection, status polling and callbacks with BTCX enabled on the exact BTCPay release before adopting it.

## Recovery requirements

- Persist a canonical block anchor `(height, hash)` and scan cursor transactionally with observations.
- On startup, compare stored anchors to node best chain; locate a common ancestor and replay forward.
- Reconcile a bounded overlap behind the cursor so missed notifications do not lose payments.
- Keep unique constraints for outpoints and invoice scripts; handle races with database upsert/transaction semantics.
- Webhook delivery is at-least-once; consumer fulfillment must be idempotent.
- Keep receiving operation available if withdrawal/signing service is stopped.
