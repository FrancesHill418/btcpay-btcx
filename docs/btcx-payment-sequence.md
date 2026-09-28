# BTCX receiving payment sequence

This is the planned receiving flow. Current plugin does not yet allocate an address, produce a payment URI, observe transactions or accept payments.

```mermaid
sequenceDiagram
    autonumber
    actor Customer
    participant X as XBoard
    participant G as BTCPay Greenfield v1
    participant P as BTCX plugin / payment handler
    participant R as Manual BTCX/CNY rate provider
    participant W as BTCX address provider
    participant C as BTCPay checkout
    participant Ph as Phoenix PoCX
    participant N as PoCX node RPC
    participant I as electrs-btcx + bindex-btcx
    participant L as BTCX hosted listener/reconciler
    participant PS as BTCPay PaymentService
    participant IW as BTCPay InvoiceWatcher

    X->>G: POST CNY invoice + orderId metadata
    G->>R: Fetch contextual BTCX/CNY rate
    R-->>G: Manual rate + timestamp/source
    G->>P: BeforeFetchingRates / ConfigurePrompt
    P->>W: Reserve unique BTCX receive address
    W->>N: Dedicated receive-only wallet/address request (prototype)
    N-->>W: Network-valid address
    W-->>P: Address + decoded scriptPubKey + tracking token
    P->>P: Snapshot CNY, BTCX, rate/source/time, network/address/script
    P-->>G: Prompt destination, amount/divisibility, persisted details/tracked destination
    G-->>X: Invoice id + checkoutLink
    X-->>Customer: Redirect to BTCPay checkout
    C-->>Customer: BTCX amount, address, btcx: URI and QR
    Customer->>Ph: Parse/import URI; validate network/address/amount
    Ph->>N: Broadcast BTCX transaction
    I->>N: Index canonical blocks and mempool
    I-->>L: Script-hash activity / candidate tx
    L->>N: Verify tx output, script, value, tx/block identity and canonical status
    L->>PS: AddPayment(outpoint, Processing) idempotently
    PS-->>IW: ReceivedPayment/invoice update notification
    L->>N: Reconcile confirmations and block hash at current tip
    alt Meets configured confirmations on canonical chain
        L->>PS: Update payment to Settled
        PS-->>IW: Settlement transition + invoice update event
        IW->>IW: Aggregate due/expiry/payment state; compute invoice status
    else Pending or insufficient confirmations
        L->>PS: Keep payment Processing
        PS-->>IW: Payment observed; invoice remains unpaid/processing
    end
    IW-->>G: Invoice status and payment events
    G-->>X: Signed BTCPay webhook
    X->>G: Retrieve invoice and independently verify event, amount, currency, BTCX method, metadata/orderId and final state
    X->>X: Idempotently fulfill only accepted settled invoice
```

> URI label is `btcx:` (the diagram uses the conceptual URI; canonical example: `btcx:pocx1qcpueamxr0aa82t7dtvhzdksq59c993f9heu9te?amount=37.5`). Phoenix v2.4.0 accepts `btcx`, `pocx`, `bitcoin`, and bare address inputs; builder should emit `btcx:` with invariant non-exponent amount formatting. This sample illustrates syntax only, not an allocated invoice.

## Responsibility boundaries

| Step | Owner | Required guarantee |
|---|---|---|
| CNY order and `metadata.orderId` | XBoard | Keep the order amount/currency authoritative; use a unique idempotency reference |
| Greenfield invoice, checkout and webhooks | BTCPay core | Persist invoice and expose its lifecycle; webhook delivery is not fulfillment authorization by itself |
| Manual quote and immutable BTCX amount | BTCX rate provider + payment handler | Validate current configured rate when creating invoice; persist rate/source/timestamp and atomic BTCX amount so later settings edits cannot alter invoice |
| Receive address/script | BTCX address provider | Unique address, correct network, exact script and durable association before payment can arrive |
| URI/QR | BTCX link and checkout extensions | Phoenix-compatible `btcx:` URI, fixed decimal up to 8 places; no binary-float/scientific notation |
| Transaction discovery | electrs/bindex candidate adapter | Wake listener with candidate; index data alone cannot settle |
| Chain truth/reconciliation | PoCX RPC plus BTCX listener | Validate output script/value, tx existence, canonical block and current confirmations; handle reorg/RBF/restarts |
| Payment records | BTCX listener + BTCPay `PaymentService` | Stable `(network, txid, vout)` identity, idempotency, `Processing`/`Settled`/`Unaccounted` updates |
| Invoice aggregation/expiry | BTCPay `InvoiceWatcher` | Aggregate validated payments and expiry; plugin does not independently mark XBoard order paid |
| Order fulfillment | XBoard | Verify signature, retrieve invoice, compare order metadata/value/currency/method/store/final state and deduplicate |

## State sequence

1. Before transaction observation: invoice remains `New` (unless BTCPay lifecycle changes it).
2. Candidate mempool output verified: create BTCX payment record `Processing`; an observed transaction is not enough to deliver goods.
3. Confirmation threshold not met, invoice underpaid, or chain/indexer state uncertain: remain processing/pending. Do not promote from stale confirmation count.
4. Canonical transaction meets configured threshold: payment becomes `Settled`; InvoiceWatcher calculates invoice status using all payments and due amount.
5. Invoice expiry/insufficient confirmed payment, replacement or reorg: payment can become `Unaccounted`/return to `Processing`; InvoiceWatcher recomputes. BTCPay may classify late funds; XBoard needs an explicit paid-late policy.
6. Only a verified final invoice status accepted by XBoard's fulfillment policy may complete an XBoard order. Repeated webhook delivery must not repeat fulfillment.

## Recovery invariants

- Notifications are hints. Reconcile durable monitored invoice scripts after listener, plugin, node or indexer restart.
- Verify exact scriptPubKey and integer atomic output amount; never use address text or rounded display values for payment matching.
- Recompute tip, block hash and confirmation count; a reorg can invalidate a previously observed/settled transaction.
- Persist every matching output separately. Duplicate delivery is a no-op; multiple outputs sum only after each independently passes validation.
- No RPC secrets/private keys in logs, API responses, payment prompts or webhook metadata.
- If node/wallet is unavailable, disable BTCX for new invoices gracefully; existing invoices remain monitored from persisted address/script data.
