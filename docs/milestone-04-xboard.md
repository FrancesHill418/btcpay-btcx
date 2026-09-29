# MILESTONE 04 — XBoard Greenfield provider integration

**Historical implementation note:** this milestone records the superseded patch-based integration. The current production integration is the standalone [`BtcpayBtcx` plugin](../integrations/xboard/BtcpayBtcx/README.md); the former patch was removed during the 2026-09-29 architecture refactor. The original XBoard `plugins-core/Btcpay` source is preserved unchanged.

Status: provider implementation and isolated provider tests complete. No development BTCPay/XBoard deployment or live webhook was available, so external runtime E2E remains unverified.

## Pinned source and change boundary

Target XBoard was `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`. The historical integration modified its existing `plugins-core/Btcpay` provider. That approach is deprecated; use the standalone plugin linked above.

## Invoice creation and order binding

The provider creates a Greenfield CNY invoice with `metadata.orderId` equal to the exact XBoard `trade_no`, `BTCX-OnChain` as the only allowed/default payment method, LowSpeed, and zero payment tolerance. It retrieves the invoice and validates store, invoice ID, metadata, CNY amount, selected BTCX method, and checkout policy before returning the checkout link. A durable unique binding records trade number, invoice ID, store, expected integer cents, currency, method, policy, and link. Retried checkout requests reuse the bound invoice rather than creating a second order invoice.

The provider creates or repairs one narrowly scoped `InvoiceSettled` webhook at XBoard's configured notify URL, with automatic redelivery. Its management record is keyed to the payment provider instance. The API token requires invoice create/view and webhook view/create/update permissions. Configure these as development secrets; values are never included in application error messages.

## Callback validation

The callback handler authenticates the exact raw request body with HMAC-SHA256 and constant-time comparison. It accepts only an `InvoiceSettled` event that is not manually marked or overpaid, matches the expected store and durable invoice binding, then retrieves the invoice directly from Greenfield. Before calling XBoard's existing paid-order service it verifies invoice ID, exact order metadata, CNY currency and cents, BTCX-only payment method, LowSpeed/zero tolerance, settled invoice and BTCX payment, and the still-unpaid order's exact current amount. Verified delivery IDs are recorded idempotently. Invalid signatures, malformed payloads, duplicate events, wrong order/currency/method, under/overpayment, and expired/partial states cannot complete an order. An explicit `Expired` invoice regression case is included in the provider test patch.

## Validation and remaining environment work

The provider test suite uses Laravel HTTP fakes and in-memory SQLite; it covers invoice options/binding/reuse, webhook registration, HMAC failures, duplicate delivery, invalid event shapes and invoice state/amount/order/payment-method validation. At implementation completion, `vendor/bin/phpunit tests/Unit/Plugins/BtcpayPluginTest.php` passed 6 tests / 26 assertions, `php -l` passed, and PHPStan reported no errors.

No live development Greenfield server, XBoard application/database, or webhook endpoint was available or contacted. External invoice retrieval, BTCPay-signed delivery, browser checkout, webhook replay through the live HTTP route, and actual XBoard order completion still need isolated development E2E. The provider tests do not claim to replace that runtime check. Apply/run instructions are in [the patch README](../integrations/xboard/README.md).
