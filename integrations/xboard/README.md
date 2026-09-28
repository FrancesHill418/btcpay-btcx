# XBoard BTCX Greenfield provider patch

This patch targets XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` and changes only the existing `plugins-core/Btcpay` provider, its plugin migration, and its provider unit tests. It does not modify XBoard core or its order/payment services.

From a clean checkout at that exact revision, apply it with:

```sh
git am 0001-btcpay-btcx-provider.patch
```

The provider creates CNY Greenfield invoices with `metadata.orderId`, selects only `BTCX-OnChain`, configures LowSpeed and zero tolerance, persists an immutable XBoard order/invoice binding, and validates signed `InvoiceSettled` callbacks by fetching the invoice from BTCPay before invoking XBoard's existing idempotent `OrderService::paid` path.

Configure a development BTCPay URL, store ID, a least-privilege Greenfield token (invoice create/view and webhook view/create/update), and a random webhook HMAC secret through XBoard's secret configuration. Do not use production credentials in tests. The provider allows plain HTTP only for loopback/private development hosts; otherwise use HTTPS.

Provider-only test command from the XBoard checkout:

```sh
vendor/bin/phpunit tests/Unit/Plugins/BtcpayPluginTest.php
```

The provider tests use Laravel's HTTP fake and an in-memory SQLite database. A real Greenfield callback, XBoard database integration, and checkout browser flow still require an isolated development deployment.
