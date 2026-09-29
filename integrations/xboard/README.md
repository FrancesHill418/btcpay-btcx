# XBoard BTCX Greenfield provider patch

This patch targets XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` and changes only the existing `plugins-core/Btcpay` provider, its plugin migration, and its provider unit tests. It does not modify XBoard core or its order/payment services.

From a clean checkout at that exact revision, apply the exact-revision patch with:

```sh
git apply --unidiff-zero 0001-btcpay-btcx-provider.patch
```

The provider creates CNY Greenfield invoices with `metadata.orderId`, selects only BTCPay's `BTCX-CHAIN` method, configures LowSpeed and zero tolerance, persists an immutable XBoard order/invoice binding, and validates signed `InvoiceSettled` callbacks by fetching the invoice from BTCPay before invoking XBoard's existing idempotent `OrderService::paid` path. A previously recorded delivery ID is a no-op: the callback returns false before re-fetching the invoice or returning an order mapping, and concurrent duplicate inserts also do not return a mapping.

Configure the BTCPay URL and store ID in the XBoard payment settings. Mount the least-privilege Greenfield token and a separate webhook HMAC key as read-only Docker secret files in the XBoard container; configure only their file paths (`btcpay_api_key_file`, `btcpay_webhook_key_file`) in XBoard. Secret values are never stored in the XBoard payment configuration, repository, Compose file or logs. Do not use production credentials in tests. The provider allows plain HTTP only for loopback/private development hosts; otherwise use HTTPS.

Provider-only test command from the XBoard checkout:

```sh
vendor/bin/phpunit tests/Unit/Plugins/BtcpayPluginTest.php
```

The provider tests use Laravel's HTTP fake, temporary mode-0600 secret files, and an in-memory SQLite database. They cover duplicate delivery no-op behavior (including skipping invoice re-fetch), underpayment, overpayment, expiry, invalid signatures, secret-path allowlisting, and invoice/order binding. At XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` with this provider patch, PHPUnit passed 7 tests / 29 assertions. An earlier isolated live XBoard + Greenfield + BTCPay BTCX regtest exact-payment flow passed with the prior provider revision; full staging E2E has not yet been rerun with this patch. Live duplicate webhook redelivery and live under/overpayment/expiry scenarios were not run. Phoenix real-device E2E remains a separate acceptance gate.
