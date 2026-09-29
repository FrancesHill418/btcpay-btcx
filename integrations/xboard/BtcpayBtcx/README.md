# BtcpayBtcx standalone XBoard payment plugin

`BtcpayBtcx` is a user plugin for XBoard. Install this directory as `XBoard/plugins/BtcpayBtcx/`; it implements XBoard's existing `PaymentInterface` and uses the existing Greenfield invoice and payment notification flows. It does not patch XBoard core or `plugins-core/Btcpay`.

The original XBoard payment plugin remains unchanged and registers **`BTCPay`**. This plugin independently registers **`BTCPayBTCX`**. Both plugin records and payment channels can be installed, enabled, disabled, configured, and used at the same time. BtcpayBtcx neither reads nor renames the original plugin's `btcpay_url`, `btcpay_storeId`, `btcpay_api_key`, or `btcpay_webhook_key` settings.

## Payment and quote behavior

XBoard order totals are CNY cents. For example, a CNY 12.34 order with a manual rate of `1 BTCX = 0.20 CNY` has a BTCX quote of 61.7 BTCX. BtcpayBtcx creates a **CNY Greenfield invoice** and forces its checkout to `BTCX-CHAIN`, `LowSpeed`, and zero tolerance. The BTCPay BTCX plugin is the quote source: its invoice prompt includes the applied manual rate, BTCX amount, timestamp, and network. BtcpayBtcx verifies those values against the independently configured rate and saves the exact invoice snapshot in its binding before returning checkout.

The configured rate is checked only when creating a new invoice. Reopening an order with an existing invoice reuses the saved invoice and binding; it does not recalculate that invoice at the current rate. Webhook handling compares the fetched invoice with the stored rate and BTCX amount snapshots and never reprices an old order.

Each XBoard order and BTCPay invoice has a unique one-to-one binding. Webhooks are accepted only for that binding and only after raw-body HMAC verification, `InvoiceSettled`, non-manual/non-overpaid policy, store/order/CNY amount checks, the BTCX payment-method snapshot, and BTCPay's current settled invoice/payment state. Only `InvoiceSettled` is authorized when the plugin registers its store webhook. Repeated verified delivery is acknowledged through XBoard's normal callback contract; `PaymentController`/`OrderService::paid` already no-ops once the order is no longer pending.

## Requirements and deployment

The server side requires:

- BTCPay Server v2.4.4 with this repository's BTCX plugin enabled.
- Bitcoin-PoCX providing the BTCPay plugin's wallet RPC.
- electrs-btcx providing the BTCPay plugin's current payment discovery/indexing endpoint.
- A store Greenfield token scoped to invoice creation/view and store webhook management.

Phoenix PoCX is a user-side wallet, not a server dependency. Do not run a Phoenix container. `bindex-btcx` is part of the electrs-btcx implementation; this integration does not require a separate production bindex container.

### Install and configure

1. Copy `integrations/xboard/BtcpayBtcx/` into `XBoard/plugins/BtcpayBtcx/`. Keep the directory name exact: the plugin manager resolves code `btcpay_btcx` to `BtcpayBtcx`.
2. In XBoard's plugin manager, install **BTCPay BTCX** (`btcpay_btcx`). Installation runs this plugin's migration from `database/migrations`; enable the plugin.
3. Add and enable a payment channel for the new **BTCPay BTCX** method. Configure its own BTCPay URL, store ID, manual BTCX/CNY rate, and `allow_mainnet` value. Do not copy values from the original BTCPay payment channel.
4. Mount two separate, read-only secret files in the XBoard runtime, for example `/run/secrets/btcpay-btcx-api-key` and `/run/secrets/btcpay-btcx-webhook-key`. Configure only these paths as `btcpay_btcx_api_key_file` and `btcpay_btcx_webhook_key_file`. The plugin rejects secret files outside `/run/secrets/` except temporary files in tests. Never put secret contents in code, plugin configuration, Compose environment, or logs.
5. Enable BTCPay's BTCX payment method for the selected store. Configure the BTCPay manual rate at the same CNY-per-BTCX value as the XBoard channel's rate; invoice creation fails closed if the returned BTCPay quote differs. Create a disposable CNY order and verify the resulting invoice's CNY value, `BTCX-CHAIN`, BTCX amount, rate, timestamp, and stored binding.
6. On first checkout the plugin creates or updates one webhook for the exact XBoard `notify_url`, store, payment UUID, and the sole authorized event `InvoiceSettled`. Confirm the callback URL uses XBoard's normal `/api/v1/guest/payment/notify/BTCPayBTCX/{uuid}` route and is public HTTPS in production.

### Mainnet gate

`btcpay_btcx_allow_mainnet` defaults to `false`. Mainnet checkout is refused unless it is explicitly enabled on the BtcpayBtcx payment configuration and the BTCPay BTCX plugin's own `BTCX:Wallet:AllowMainnet` gate is enabled. BTCPay rejects mainnet in Development and Staging regardless of opt-in. Production checkout, BTCPay, notify, and checkout links must use HTTPS. Mainnet still requires separate manual operational acceptance; a configuration flag is not acceptance evidence.

### Upgrade, disable, and uninstall

The previous patch-based XBoard integration is deprecated and is not an installation method. Do not apply `0001-btcpay-btcx-provider.patch` or modify `plugins-core/Btcpay/Plugin.php`. Install BtcpayBtcx alongside the original plugin instead. Existing original BTCPay payment records, configuration, invoices, and webhook behavior are not migrated or altered. Existing orders/invoices created by the old BTCX patch do not acquire new BtcpayBtcx bindings; reconcile or close those orders under the site's existing operational procedure before switching new checkouts.

Disabling BtcpayBtcx leaves the original BTCPay payment method available. XBoard's plugin manager rolls back only this plugin's migration on uninstall, which drops only `btcpay_btcx_invoice_bindings`, `btcpay_btcx_webhook_registrations`, and `btcpay_btcx_webhook_deliveries`. Back up and reconcile outstanding BTCX orders before uninstall because its own invoice bindings and delivery ledger are removed by that rollback. No original BTCPay database tables or historical invoices are modified.

## Tests

From the XBoard checkout with its Composer dependencies installed, run:

```sh
vendor/bin/phpunit plugins/BtcpayBtcx/Tests/BtcpayBtcxPluginTest.php
```

The suite covers CNY/BTCX amount calculation and immutable snapshots, invoice/order uniqueness, secret-file policy, HMAC and event validation, amount/currency/payment-method/order rejection, duplicate webhook behavior, webhook registration/scope, mainnet default-off, HTTPS checkout validation, migration isolation, and simultaneous `BTCPay`/`BTCPayBTCX` registration against the pinned original provider source.
