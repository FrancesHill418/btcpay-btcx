# TASK 03.7 — Complete BTCX Payment Method

Status: implementation complete for the plugin payment flow; live regtest and BTCPay PostgreSQL validation remain in final acceptance.

## Invoice creation and immutable snapshot

BTCPay creates its CNY invoice and applies the configured administrator-managed BTCX/CNY rate. The handler rejects a disabled/missing/invalid rate or a rate changed during invoice creation, converts the due amount to integer BTCX atomic units, obtains a unique wallet address, and persists an invoice snapshot containing:

- fiat amount and currency;
- BTCX amount and currency;
- BTCX/CNY rate, source (`manual`) and UTC rate timestamp;
- atomic BTCX amount;
- network, receive address and exact scriptPubKey.

The snapshot is not changed after invoice creation. Subsequent rate edits apply only to new invoices. Legacy timestamp forms are handled by the BTCX property converter; malformed or inconsistent address/script/amount details fail closed when constructing a payment link or reconciling a payment.

## Payment and state flow

The receive address is an invoice-unique wallet allocation and BTCPay persists its tracked destination. The canonical Phoenix URI/QR is derived only from the saved snapshot. Electrs-btcx discovers history; PoCX node RPC validates transactions and output script/amount; BTCPay persists each output under `{network, txid, vout}` and makes repeat delivery idempotent.

All partial payments are separately recorded and BTCPay's `InvoiceWatcher` aggregates them. Its core invoice accounting determines underpayment, exact payment, overpayment, expired and late invoice states. The plugin follows BTCPay `PaymentService` and invoice `SpeedPolicy` for `Processing`, `Settled`, and `Unaccounted`; a complete reorged/dropped output can be reversed. XBoard fulfillment is not part of this plugin task and must independently validate invoice status, expiry and amount before fulfillment.

Rate/amount/address data is immutable invoice prompt data. Payment identity, confirmation details and payment status use BTCPay's persisted Payments table and invoice lifecycle records rather than mutating the quote snapshot. This separates the frozen exchange quote from evolving chain observations while preserving all required invoice state durably.

## Verification boundary

Unit tests cover rate/atomic conversion, unique and recoverable address labels, URI fixed precision, duplicate outpoint observations, exact/under/overpayment output values, split/multiple outputs, BTCPay SpeedPolicy mapping, canonical block checks, and reorg status decisions. At this point tests use mock wallet/node/indexer clients. PostgreSQL-backed PaymentService idempotency, live InvoiceWatcher aggregation, regtest confirmations/reorgs, and Phoenix QR scanning remain required under MILESTONE 06 development acceptance. No mainnet or production credentials were used.
