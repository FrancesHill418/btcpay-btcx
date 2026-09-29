# Historical XBoard provider review

This checklist reviewed the superseded patch-based integration and is not a current implementation guide. Current production/staging installation uses the independent [`BtcpayBtcx` plugin](../integrations/xboard/BtcpayBtcx/README.md) and must not modify `plugins-core/Btcpay/Plugin.php`. Retain this file only as historical review context; use the standalone plugin tests and current deployment checklist for release evidence.

# Greenfield webhook validation and XBoard payment state

**Pinned versions:** BTCPay Server `v2.4.4` (`2d5a0d8077bb33af080e949031da33d84b80638d`); XBoard `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`. Findings below are source-based. XBoard production files were not modified.

## Current XBoard behavior

`plugins-core/Btcpay/Plugin.php::notify()` currently:

1. Reads `request()->getContent()`, trims the body, reads `Btcpay-Sig`, and checks `sha256=` plus `hash_hmac('sha256', payload, configured_secret)` using `hashEqual()`/`hash_equals()`.
2. Uses the event's `invoiceId` to GET `/api/v1/stores/{storeId}/invoices/{invoiceId}` with an API key.
3. Takes the returned invoice `metadata.orderId` as XBoard `trade_no` and the event `invoiceId` as `callback_no`.
4. Returns those two strings to `PaymentController::notify()`, which marks a pending order paid.

### Validation matrix

| Check | Current behavior | Finding |
|---|---|---|
| HMAC | Yes, SHA-256 and constant-time `hash_equals` when available | Raw body is trimmed before verification; BTCPay signs the exact request bytes. Verify the original raw bytes unchanged. Header lookup uses `getallheaders()` and an exact key casing; normalize header names through the framework request API. |
| Event type | No | Any correctly signed event carrying an invoice ID, including `InvoiceCreated`, can currently reach the paid path. Critical fulfillment vulnerability. |
| Invoice ID | Only used to build the retrieve URL | Validate presence/type/length, safely encode it as a URL segment, verify GET success and response shape, and require retrieved `id` to equal the event `invoiceId`. Current code does not check HTTP status or robustly handle invalid JSON/null response. |
| Metadata `orderId` | Reads it from retrieved invoice | Does not check it exists, is a scalar, matches one XBoard order, or is already bound to that invoice. It must be the sole trusted order correlation value only after all other checks pass. |
| Invoice amount | No | Compare retrieved decimal `amount` and `paidAmount` against a durable expected value under explicit under/overpayment policy. Current order amount alone is CNY cents; use exact decimal/integer arithmetic. |
| Currency | No | Validate configured invoice currency (`CNY` recommended) and reject mismatch. |
| Payment method | No | Validate the retrieved invoice's configured methods/payment details and, for payment events, event `paymentMethodId`; require BTCX chain method exactly. |
| Invoice state | No | A validly signed `InvoiceCreated`, `InvoiceReceivedPayment`, or `InvoiceProcessing` must not trigger fulfillment. Retrieve and validate terminal state. |
| Required confirmations | No | BTCPay `InvoiceSettled` is the configured invoice-level settlement event; `InvoicePaymentSettled` means a payment settled and can occur per payment. Current provider ignores both distinctions. |
| Expiry / late payment | No | Current code ignores `expirationTime`, `monitoringExpiration`, event `afterExpiration`, and current state. Late/partial payment must follow a defined review/refund policy. |
| Event duplicate | No delivery-ID dedupe | `OrderService::paid()` locks the order and only transitions `PENDING` once, then synchronously dispatches `OrderHandleJob`, so repeated events do not activate the same order twice. This protects fulfillment idempotency but does not prevent invalid first events or provide webhook-delivery audit. Persist/dedupe `deliveryId` and record invoice/order binding. |
| HTTP method | Route accepts GET and POST | BTCPay sends POST. Restrict this endpoint to POST in future changes and return a clear non-2xx response for failed verification. |

`PaymentController::handle()` considers any provider response with trade/callback numbers verified; if the order is pending it calls `OrderService::paid()`. `OrderService::paid()` does row locking, pending-state check, and one-time dispatch, but it cannot make an under-validated provider response trustworthy.

## BTCPay v2.4.4 event and invoice states

Source of truth: v2.4.4 `Plugins/Webhooks/TriggerProviders/InvoiceTriggerProvider.cs`, `Services/Invoices/InvoiceEntity.cs`, invoice/webhook OpenAPI schemas, and tests in `BTCPayServer.Tests/{WebhooksTests,GreenfieldAPITests,UnitTest1}.cs`.

| Webhook event | v2.4.4 source meaning | XBoard action |
|---|---|---|
| `InvoiceCreated` | Invoice created; event carries invoice identity and metadata | Record/check invoice association only; never fulfill |
| `InvoiceReceivedPayment` | A payment was seen; includes `paymentMethodId`, payment detail, and `afterExpiration` | Record pending payment observation; never fulfill based only on this event |
| `InvoiceProcessing` | Triggered by `PaidInFull`; full amount observed but invoice not yet in settled state/confirmation policy may still be pending | Show/retain processing; do not fulfill |
| `InvoicePaymentSettled` | A particular payment event became settled; event includes payment and method detail | Reconcile observation; invoice may still not be overall settled in split-payment/exception cases, so do not fulfill from this alone |
| `InvoiceSettled` | Triggered by invoice `Confirmed` or explicitly `MarkedCompleted`; OpenAPI says enough configured confirmations for on-chain invoices and merchant may proceed | Fulfill only after GET verifies current invoice state `Settled`, exact invoice/store/order/currency/amount/method match, and manual-mark policy passes |
| `InvoiceExpired` | Invoice expiration; event says whether partially paid | Do not fulfill; apply expiry and partial-payment review/refund policy |
| `InvoiceInvalid` | Failed to confirm in time or manually marked invalid; payload identifies manual marking | Do not fulfill; flag for review/refund handling |

The invoice state API in v2.4.4 represents `New`, `Processing`, `Settled`, `Expired`, or `Invalid` (with `additionalStatus` for exception details). `InvoiceSettled` payload includes `manuallyMarked` and `overPaid`; if operators may manually mark complete, the XBoard policy must explicitly accept or reject this signal. The recommended default is to accept automatic settlement only (`manuallyMarked == false`) unless a separately authorized reconciliation path handles manual completion.

BTCPay's `InvoiceSettled` means the configured invoice-level confirmation/settlement policy is met, not necessarily an irreversible finality guarantee. BTCX reorg handling and post-fulfillment compensation still need explicit policy.

### Recommended BTCPay-to-XBoard state mapping

| BTCPay state/event | XBoard order state/action |
|---|---|
| `New` / `InvoiceCreated` | Keep `STATUS_PENDING` (待支付). |
| Payment event / `InvoiceReceivedPayment` | Keep `STATUS_PENDING`; optionally expose a separate “payment detected” display state, but do not call `paid()`. |
| `Processing` / `InvoiceProcessing` | Keep `STATUS_PENDING` while BTCPay awaits settlement. XBoard's `STATUS_PROCESSING` means entitlement provisioning (“开通中”), not chain confirmation, so do not map these states together. |
| `InvoicePaymentSettled` | Reconcile the individual payment; keep the order pending until the retrieved invoice itself is `Settled`. |
| Verified `Settled` / `InvoiceSettled` | Call `OrderService::paid()` once: XBoard moves `PENDING` → `PROCESSING`; its synchronous `OrderHandleJob` runs `OrderService::open()`, which provisions and advances the order to `COMPLETED`. |
| `Expired` with no payment | Do not fulfill; apply an explicit pending-order expiry/cancel policy. |
| `Invalid`, partial expiry, late payment, or unexpected overpayment | Do not fulfill automatically; preserve payment evidence and route to review/refund/compensation handling. XBoard has no dedicated payment-review state in the inspected source. |

The order status and invoice payment state have different meanings: an invoice that is `Processing` must not set XBoard's `STATUS_PROCESSING` because that state triggers provisioning.

## Recommended webhook flow

```text
POST body + BTCPay-Sig
  → preserve raw bytes; compute HMAC-SHA256 and constant-time compare
  → parse JSON only after signature passes; allowlist event type
  → validate deliveryId, invoiceId, storeId and payload shape
  → retrieve invoice using scoped API key over TLS
  → assert retrieved id/storeId and metadata.orderId match the payload and stored order binding
  → assert expected invoice currency, amount/paidAmount and BTCX payment method
  → assert event/state/expiry/settlement policy; reject processing/partial/late/non-settled events for fulfillment
  → atomically record deliveryId + invoice/order + verified state; deduplicate replay
  → transition one pending XBoard order and dispatch fulfillment once
  → return 2xx only after durable acceptance; retry on transient retrieval/storage errors
```

### Responsibility boundary

| BTCPay supplies | XBoard provider must still verify |
|---|---|
| HTTPS callback configured by store operator; signed body with per-webhook secret | HMAC over exact raw bytes, correct secret and constant-time equality |
| `type`, `deliveryId`, `invoiceId`, `storeId`, timestamp, metadata; event-specific payment fields | Allowed event for requested action, schema/type checks, duplicate/replay handling, expected store and invoice binding |
| Invoice retrieval endpoint with permission checks; invoice state, amount, currency, checkout config, metadata, expiry | API status/body validation, request/response invoice identity, order metadata binding, expected invoice/order amount and currency |
| For `InvoiceSettled`, configured BTCPay invoice settlement threshold is met unless the event is manually marked | BTCX payment method requirement, merchant manual-mark policy, terminal state and partial/overpaid/late payment policy |
| At-least-once webhook delivery/retry and delivery identifiers | Atomic local acceptance, idempotent XBoard fulfillment, durable audit trail and correct 2xx/retry behavior |

BTCPay does not know XBoard's expected CNY order total, XBoard `trade_no`, entitlement rules, or merchant's BTCX reorg compensation decision. XBoard must validate those against durable state.

## Minimal future XBoard change surface (design only)

- `plugins-core/Btcpay/Plugin.php::pay()`: handle Greenfield errors/timeouts, pass only intended BTCX method if configured, save/bind invoice ID and expected invoice denomination/amount, and stop ignoring the `notify_url` supplied by `PaymentService` (or clearly register webhooks elsewhere).
- `plugins-core/Btcpay/Plugin.php::notify()`: use exact raw request body and normalized header access; validate event allowlist and payload; safely GET and validate invoice; enforce store/invoice/order/currency/amount/method/status/expiry/manual-mark policy; persist delivery idempotency; return no verified trade number on any failure.
- `plugins-core/Btcpay/Plugin.php::form()` and `plugins-core/Btcpay/config.json`: expose/validate invoice currency, BTCX payment method ID, API key scope, webhook URL/secret and registration mode; protect secrets.
- `app/Http/Routes/V1/GuestRoute.php::map()`: make the payment notify route POST-only.
- `app/Http/Controllers/V1/Guest/PaymentController::notify()/handle()`: preserve the current order lock/state transition but only accept a strongly validated provider result; make transient API/storage errors retryable rather than logging and acknowledging as a successful notification.
- Persistence migration/model: keep invoice ID ↔ XBoard `trade_no`, expected currency/amount/rate reference, and unique BTCPay `deliveryId`/original delivery identity. Existing `orders.callback_no` stores only one callback identifier and is not a webhook-delivery ledger.

No listed file was changed in this task.

Pinned refs: [BTCPay webhook sender](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Plugins/Webhooks/WebhookSender.cs), [BTCPay event mapping](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Plugins/Webhooks/TriggerProviders/InvoiceTriggerProvider.cs), [webhook OpenAPI schemas](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/wwwroot/swagger/v1/swagger.template.webhooks.json), [XBoard provider](https://github.com/cedar2025/Xboard/blob/4f48e61a2cbc6db5338872b6bdb45ef954ec1256/plugins-core/Btcpay/Plugin.php), [XBoard notify controller](https://github.com/cedar2025/Xboard/blob/4f48e61a2cbc6db5338872b6bdb45ef954ec1256/app/Http/Controllers/V1/Guest/PaymentController.php), [XBoard order transition](https://github.com/cedar2025/Xboard/blob/4f48e61a2cbc6db5338872b6bdb45ef954ec1256/app/Services/OrderService.php).
