# BTCPay 2.4.4 × XBoard Greenfield compatibility audit

**Scope:** architecture and source compatibility only. No BTCPay core, XBoard production source, or BTCX plugin was changed. The BTCPay and XBoard source checkouts were fetched under `/tmp` at immutable refs and are not project dependencies.

## Fixed BTCPay target

| Item | Verified value |
|---|---|
| Release tag | `v2.4.4` |
| Commit | `2d5a0d8077bb33af080e949031da33d84b80638d` |
| Commit date | 2026-09-07 |
| Target framework | `net10.0` (`Build/Common.csproj`); C# language version 14 |
| SDK | .NET 10. The source has no SDK version pin in `global.json`; the v2.4.4 release workflow builds plugin compatibility with SDK image `10.0.301`. This VPS has SDK `10.0.401`, also .NET 10. |
| Plugin API | BTCPay plugin API from this same source tag: `BaseBTCPayServerPlugin.Execute(IServiceCollection)`, `IPaymentMethodHandler`, and payment/link/checkout extension contracts. A plugin must target the matching BTCPay dependency; this audit does not compile a plugin. |
| Greenfield API | REST API `v1`, served under `/api/v1`; the pinned source includes the v1 OpenAPI templates. |

The earlier `docs/baseline.md` entry recorded v2.4.1 as a proposal based on the 2026-09-26 audit. TASK 01.6 explicitly fixes v2.4.4; that release is a different, later tag and this audit uses its exact commit. No branch-head or `master` source was substituted. The official [v2.4.4 release announcement](https://blog.btcpayserver.org/btcpay-server-2-4-4/) describes the September 2026 security release and breaking changes.

Pinned primary source references: [v2.4.4 common target](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/Build/Common.csproj), [v2.4.4 release workflow](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/.github/workflows/release.yml), [Greenfield invoice OpenAPI](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/wwwroot/swagger/v1/swagger.template.invoices.json), [Greenfield webhook OpenAPI](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/wwwroot/swagger/v1/swagger.template.webhooks.json).

## XBoard order-to-checkout path

The specified XBoard source is commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` (`fix(order): prevent duplicate refunds and redemptions`, 2026-08-30).

1. `app/Http/Controllers/V1/User/OrderController::save()` calls `OrderService::createFromRequest()` to persist an order with a `trade_no` and integer CNY minor-unit `total_amount`. `OrderController::checkout()` loads the enabled payment record and calls `PaymentService::pay()`.
2. `app/Services/PaymentService::pay()` passes `trade_no`, `total_amount`, and a generated XBoard `notify_url` to the provider.
3. `plugins-core/Btcpay/Plugin.php`, class `Plugin`, method `pay()`, converts the amount from cents to CNY, sends `POST /api/v1/stores/{storeId}/invoices` with `metadata.orderId = trade_no`, then returns `checkoutLink` to XBoard's checkout response.
4. A Greenfield webhook must be registered on BTCPay for that store, targeting XBoard's generated `/api/v1/guest/payment/notify/{method}/{uuid}` URL. The current `Plugin::pay()` does not use the passed `notify_url` and does not call the webhook-registration endpoint; the configured callback therefore has to be set up separately today.
5. `app/Http/Routes/V1/GuestRoute::map()` allows GET and POST to the notify route. `app/Http/Controllers/V1/Guest/PaymentController::notify()` resolves the configured provider, calls `Plugin::notify()`, and if it returns a trade number passes it to `handle()`/`OrderService::paid()`.

This confirms the intended high-level path:

```text
XBoard order → Greenfield create invoice → BTCPay invoice → checkoutLink
             → Greenfield webhook → XBoard notify → pending order transitions to processing
```

## Greenfield behavior in v2.4.4

### Authentication and permissions

`BTCPayServer/Security/GreenField/APIKeysAuthenticationHandler.cs` authenticates API keys; the v1 OpenAPI security scheme specifies `Authorization: token {token}`. Invoice creation requires `btcpay.store.cancreateinvoice`; invoice retrieval requires `btcpay.store.canviewinvoices`; webhook creation requires `btcpay.store.webhooks.canmodifywebhooks`. Use a narrowly scoped key that can create and read invoices and create/manage the one required store webhook. XBoard currently uses the correct `token ` format for both create and retrieve calls.

### Invoice creation and retrieval

`GreenfieldInvoiceController.CreateInvoice()` handles `POST /api/v1/stores/{storeId}/invoices`. It validates nonnegative amount, validates any explicit checkout payment method against a registered handler, enforces a 30-second minimum requested expiration, then delegates invoice creation to `UIInvoiceController.CreateInvoiceCoreRaw()`.

`GET /api/v1/stores/{storeId}/invoices/{invoiceId}` is also served by `GreenfieldInvoiceController.GetInvoice()`. It returns 404 for an unknown invoice and otherwise serializes `id`, `storeId`, `amount`, `paidAmount`, `currency`, `checkoutLink`, `createdTime`, `expirationTime`, `monitoringExpiration`, `status`, `additionalStatus`, `metadata`, and checkout/payment-method information. `checkoutLink` is generated from the invoice ID and request host; it is not a custom XBoard URL.

The invoice request schema supports `amount`, `currency`, `metadata`, and checkout options. `metadata.orderId` is allowed and comes back both in invoice retrieval and in the webhook payload. If `checkout.paymentMethods` is omitted, the store's active payment-method configuration is used. XBoard currently omits this field, so it cannot select BTCX explicitly; BTCX must be enabled on the store or XBoard's provider must later send its `PaymentMethodId`.

### Webhook registration and callback

`POST /api/v1/stores/{storeId}/webhooks` is implemented by `GreenfieldStoreWebhooksController.CreateWebhook()`. The body accepts `url`, `secret`, `enabled`, `automaticRedelivery`, and `authorizedEvents` (`everything` or `specificEvents`). The response includes the webhook ID and secret. The event list in the v2.4.4 schema includes `InvoiceCreated`, `InvoiceReceivedPayment`, `InvoiceProcessing`, `InvoiceExpired`, `InvoiceSettled`, `InvoiceInvalid`, and `InvoicePaymentSettled`.

`WebhookSender.SendDelivery()` serializes the event body to bytes, calculates HMAC-SHA256 with the configured webhook secret over those exact bytes, lower-case hex encodes the digest, and sends POST JSON with `BTCPay-Sig: sha256={hex}`. Invoice webhook bodies carry `deliveryId`, `webhookId`, `originalDeliveryId`, `isRedelivery`, `type`, `timestamp`, `storeId`, `invoiceId`, and `metadata`; payment-related event types additionally carry payment/method details. A retried delivery may have a new `deliveryId` plus an `originalDeliveryId`; consumers should deduplicate safely and not assume exactly-once delivery.

## CNY and payment-method compatibility

The v2.4.4 currency table contains CNY with two decimal places. Greenfield accepts an invoice currency, and `CreateInvoiceCoreRaw()` validates/rounds the amount using the currency data, runs configured rate rules for active payment methods, and records calculated prompts/rates on the invoice. Therefore a CNY invoice with a BTCX on-chain payment method is supported by the API/data model if and only if the plugin registers the method/currency and BTCPay can obtain a valid BTCX/CNY rate. CNY's presence proves the fiat invoice denomination, not that any enabled provider can quote BTCX.

BTCPay's standard on-chain extension uses `BTCPayNetwork` and `BitcoinLikePaymentHandler`, which assumes NBitcoin/NBXplorer-style chain integration. BTCX's PoCX-specific block/header behavior is not proven compatible with that standard watcher. A BTCX plugin may need its own `IPaymentMethodHandler` and payment listener/reconciler feeding BTCPay's invoice payment service, with `IPaymentLinkExtension` and `ICheckoutModelExtension` for invoice links and checkout presentation. The plugin must report payments through BTCPay's payment pipeline; it must not write core invoice tables directly.

The actual BTCX/CNY rate source and BTCX chain-to-BTCPay event path remain unproven. See [currency-flow.md](currency-flow.md) and [architecture-decision.md](architecture-decision.md).

## BitPay-compatible API comparison

BTCPay v2.4.4 still ships a BitPay-compatible invoice API in `BTCPayServer/Plugins/Bitpay/Controllers/BitpayInvoiceController.cs`. The compatibility API uses BitPay-specific request/response fields, state names, and IPN behavior; v2.4.4 also exposes `IPaymentMethodBitpayAPIExtension` for payment methods that participate in that API. A custom BTCX method would need that compatibility surface validated in addition to its Greenfield handler.

XBoard's provider already speaks Greenfield: the endpoint, `metadata.orderId`, `checkoutLink`, invoice retrieval, and HMAC webhook pattern are direct matches. Replacing it with BitPay would require an adapter rewrite and BTCX BitPay serialization/IPN work without adding functionality needed by this flow. **Use Greenfield; do not implement BitPay for this integration.**

## Runtime proof status

The immutable BTCPay source and its v2.4.4 `GreenfieldAPITests.cs`/`WebhooksTests.cs` were inspected, including tests for invoice creation, payment event ordering, event payloads, and settled/invalid state. No XBoard-to-BTCPay end-to-end instance was started in this task: the VPS has Docker but no PHP CLI, Composer, installed XBoard dependencies, BTCPay test stack, or BTCX/mock payment-method plugin. Source-level compatibility is therefore documented; create-invoice, webhook registration/delivery, and XBoard signature handling still require an isolated runtime smoke test before any production use.

## Pinned source paths

- BTCPay: `BTCPayServer/Controllers/GreenField/GreenfieldInvoiceController.cs`, `BTCPayServer/Controllers/UIInvoiceController.cs`, `BTCPayServer/Plugins/Webhooks/Controllers/GreenfieldStoreWebhooksController.cs`, `BTCPayServer/Plugins/Webhooks/WebhookSender.cs`, `BTCPayServer/Plugins/Webhooks/TriggerProviders/InvoiceTriggerProvider.cs`, and `BTCPayServer.Tests/{GreenfieldAPITests,WebhooksTests}.cs` at [commit `2d5a0d8`](https://github.com/btcpayserver/btcpayserver/tree/2d5a0d8077bb33af080e949031da33d84b80638d).
- XBoard: `plugins-core/Btcpay/Plugin.php`, `plugins-core/Btcpay/config.json`, `app/Services/PaymentService.php`, `app/Http/Controllers/V1/User/OrderController.php`, `app/Http/Controllers/V1/Guest/PaymentController.php`, `app/Http/Routes/V1/GuestRoute.php`, and `app/Services/OrderService.php` at [commit `4f48e61`](https://github.com/cedar2025/Xboard/tree/4f48e61a2cbc6db5338872b6bdb45ef954ec1256).
