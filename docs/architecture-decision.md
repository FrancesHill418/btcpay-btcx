# Architecture decision: BTCPay 2.4.4 and XBoard Greenfield

**Status:** architecture decision from TASK 01.6; implementation status has since advanced through TASK 02.2. Current project state and blockers are in [PROJECT-STATE.md](PROJECT-STATE.md). Compatibility basis is BTCPay Server `v2.4.4` at `2d5a0d8077bb33af080e949031da33d84b80638d` and XBoard `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`.

## API

**Choose Greenfield API v1. Do not add a BitPay adapter.** XBoard's current `plugins-core/Btcpay/Plugin.php` already uses Greenfield invoice creation, `metadata.orderId`, `checkoutLink`, invoice retrieval, and a signed webhook. Greenfield's v2.4.4 invoice API can expose a plugin-registered BTCX payment method. The BitPay compatibility API is a second contract with its own request/response/IPN adapter; BTCX would need additional `IPaymentMethodBitpayAPIExtension` support and compatibility testing. That would add maintenance without solving a current XBoard limitation.

## Invoice currency

**Choose CNY invoice + BTCX payment method (Option A).** XBoard owns CNY order totals; BTCPay v2.4.4 includes CNY at two decimal precision and locks rates/payment prompts during invoice creation. Greenfield creates a CNY invoice and returns its checkout link. This preserves current order/accounting semantics and lets BTCPay own expiry, checkout and invoice state.

This choice is conditional on the BTCX plugin providing a valid BTCX/CNY rate and a working BTCX payment method. If that cannot be supplied safely, BTCX checkout is blocked; do not silently substitute a BTCX invoice or estimate.

Option B (XBoard converts CNY to a BTCX amount and creates a BTCX invoice) is technically possible once the plugin registers BTCX as a currency/method. It shifts conversion, amount persistence, rounding, rate timestamp and expiry reconciliation into XBoard and is not the recommended first integration.

## Rate

**First release: administrator-managed manual BTCX/CNY rate through the plugin's `IContextualRateProvider`.** It reads enabled/rate/updatedAt configuration for each contextual request and snapshots the rate into each invoice prompt. No live market source is used. This design and implementation are documented in [manual-rate.md](manual-rate.md) and [plugin-architecture.md](plugin-architecture.md).

The manual rate is a configured reference value, not an executable market quote. It must be valid and configured before BTCX invoice creation. A future market-rate mode would need its own reviewed source and stale/abnormal/failure policy. Existing invoice snapshots are not repriced when the administrator changes the rate.

## Webhook

Register the store webhook for **`InvoiceSettled`** as the only fulfillment event. BTCPay v2.4.4 emits this for invoice-level confirmation/settlement; an event may be manually marked, so XBoard must enforce the explicit policy (`manuallyMarked == false` by default). Also subscribe to `InvoiceCreated`, `InvoiceReceivedPayment`, `InvoiceProcessing`, `InvoicePaymentSettled`, `InvoiceExpired`, and `InvoiceInvalid` for reconciliation/status/audit if wanted, but they must never independently fulfill an order.

On `InvoiceSettled`, validate exact raw-body `BTCPay-Sig`, event type, delivery/invoice/store IDs, retrieved invoice identity/state, `metadata.orderId`, expected CNY currency/amount, BTCX method/payment, expiry, paid amount, overpayment/manual-mark policy, and replay/idempotency record before transitioning an order. BTCPay confirms event provenance and its configured invoice settlement threshold; it does not validate XBoard's order amount, entitlement policy, BTCX-specific reorg compensation, or local replay ledger.

## XBoard changes

No XBoard code is changed in TASK 01.6. Future minimum file/method scope:

- `plugins-core/Btcpay/Plugin.php::pay()`: robust Greenfield request/error handling, method selection, invoice/order binding, expected currency/amount tracking, and webhook target registration or explicit registration tooling.
- `plugins-core/Btcpay/Plugin.php::notify()`: raw-byte HMAC, case-insensitive header handling, event allowlist, safe invoice retrieval and identity checks, metadata/order/currency/amount/method/state/expiry/manual-mark verification, and delivery dedupe.
- `plugins-core/Btcpay/Plugin.php::form()` and `plugins-core/Btcpay/config.json`: validated endpoint, scoped Greenfield key, store ID, currency, BTCX method ID, webhook secret and registration mode.
- `app/Http/Routes/V1/GuestRoute.php::map()`: POST-only payment webhook route.
- `app/Http/Controllers/V1/Guest/PaymentController::notify()/handle()`: accept only provider-validated settlement and keep transactionally idempotent order fulfillment/error retries.
- A migration/model or dedicated payment mapping: persist invoice ID ↔ order trade number, expected denomination/amount/rate reference and unique webhook `deliveryId`.

Existing `OrderService::paid()` row-locks an order and only transitions a pending order once, so fulfillment is idempotent against repeated callbacks after the first transition. It is not a substitute for validating the callback before calling it.

## BTCPay changes

Do not modify BTCPay Server core. The future BTCX plugin should target the v2.4.4 plugin API and register these extension points:

- `BaseBTCPayServerPlugin.Execute(IServiceCollection)` for plugin DI registration and BTCX network/rate/payment services.
- `IPaymentMethodHandler` with `PaymentMethodId` for BTCX, `BeforeFetchingRates`, `ConfigurePrompt`, `AfterSavingInvoice`, and serialization/config parsing. Use BTCPay's `PaymentService.AddPayment()` pipeline for observed payments and its invoice event state machine; do not write core tables directly.
- BTCX chain/network/address metadata and plugin-owned listener/reconciler. Do not assume stock `BitcoinLikePaymentHandler`/NBXplorer works with PoCX-specific headers until proven.
- `IPaymentLinkExtension` and `ICheckoutModelExtension` (or matching v2.4.4 extension contracts) for BTCX payment URI and checkout amount/address/status presentation.
- `IRateProvider` plus registered BTCX rate rules/currency metadata so CNY invoice creation can calculate BTCX due amount. Rate source/quality/failure policy must be reviewed first.
- Optional `IPaymentMethodBitpayAPIExtension` only if BitPay compatibility later becomes a separate requirement; it is not needed under this decision.

No plugin implementation is included here.

## Blockers and validation gates

1. **No spend destination or payment pipeline:** the plugin's payment link is still null; no BTCX address, URI, payment monitoring, or settlement exists. The manual quote provider does not make the invoice payable.
2. **BTCX payment pipeline unproven:** BTCPay's built-in on-chain handler assumes standard NBitcoin/NBXplorer behavior. PoCX/BTCX custom header and indexer compatibility must be validated; custom listener/reconciliation may be required.
3. **Existing XBoard provider is unsafe to fulfill:** it accepts any validly signed invoice event as a paid callback and omits state/currency/amount/method/expiry checks. No production payments should use it for fulfillment until the validation flow above is implemented and tested.
4. **XBoard end-to-end webhook remains unproven:** TASK 02.1 exercised the plugin inside the pinned BTCPay test host, but did not run XBoard or deliver a webhook through its provider/controller. Do not treat the Greenfield plugin smoke test as proof of XBoard integration; an isolated XBoard callback test is still needed before production use.
5. **Manual webhook wiring:** XBoard generates a `notify_url`, but the current BTCPay provider ignores it and never registers a webhook. Operator setup or future provider tooling must make webhook URL/secret configuration explicit.

## Decision summary

| Decision point | Choice |
|---|---|
| API | Greenfield v1 |
| Invoice denomination | CNY |
| BTCX due calculation | BTCX plugin custom rate provider, with BTCPay invoice-time lock |
| Fulfillment event | `InvoiceSettled`, after retrieved invoice and order checks; reject manual mark by default |
| XBoard adaptation | Harden existing Greenfield provider/notify and persist invoice/event identity |
| BTCPay core | No changes |
| TASK 02 | Completed; runtime integration recorded in [runtime-smoke-test.md](runtime-smoke-test.md). |
| TASK 02.2 | Source compatibility audit and test design recorded in [Phoenix PoCX audit](phoenix-pocx-compatibility.md); no wallet runtime round trip was performed. |

## References

- [BTCPay v2.4.4 source commit](https://github.com/btcpayserver/btcpayserver/tree/2d5a0d8077bb33af080e949031da33d84b80638d)
- [BTCPay v2.4.4 official release announcement](https://blog.btcpayserver.org/btcpay-server-2-4-4/)
- [XBoard pinned source commit](https://github.com/cedar2025/Xboard/tree/4f48e61a2cbc6db5338872b6bdb45ef954ec1256)
- Related audit detail: [API compatibility](api-compatibility.md), [currency flow](currency-flow.md), [webhook validation](webhook-validation.md).
