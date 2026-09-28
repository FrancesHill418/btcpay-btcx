# XBoard BTCX Greenfield provider patch

This patch targets XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` and changes only the existing `plugins-core/Btcpay` provider, its plugin migration, and its provider unit tests. It does not modify XBoard core or its order/payment services.

From a clean checkout at that exact revision, apply it with:

```sh
git am 0001-btcpay-btcx-provider.patch
```

The provider creates CNY Greenfield invoices with `metadata.orderId`, selects only BTCPay's `BTCX-CHAIN` method, configures LowSpeed and zero tolerance, persists an immutable XBoard order/invoice binding, and validates signed `InvoiceSettled` callbacks by fetching the invoice from BTCPay before invoking XBoard's existing idempotent `OrderService::paid` path.

Configure a development BTCPay URL, store ID, a least-privilege Greenfield token (invoice create/view and webhook view/create/update), and a random webhook HMAC secret through XBoard's secret configuration. Do not use production credentials in tests. The provider allows plain HTTP only for loopback/private development hosts; otherwise use HTTPS.

Provider-only test command from the XBoard checkout:

```sh
vendor/bin/phpunit tests/Unit/Plugins/BtcpayPluginTest.php
```

The provider tests use Laravel's HTTP fake and an in-memory SQLite database. They cover duplicate delivery deduplication, underpayment, overpayment, expiry, invalid signatures, and invoice/order binding. The test group passed 6 tests / 25 assertions. An isolated live XBoard + Greenfield + BTCPay BTCX regtest exact-payment flow also passed; live duplicate webhook redelivery and live under/overpayment/expiry scenarios were not run. Phoenix real-device E2E remains a separate acceptance gate.
