# TASK 01.6.1 Greenfield and XBoard smoke test

**Run date:** 2026-09-27. **Environment:** isolated development test harness only; no production endpoint or wallet was used. No XBoard production source or BTCPay core source was changed. XBoard checkout was pinned to `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`; BTCPay was v2.4.4, commit `2d5a0d8077bb33af080e949031da33d84b80638d`.

## Result summary

| Check | Result | Evidence |
|---|---|---|
| BTCPay Greenfield create/get/webhook semantics | Passed in the pinned release's own integration test | `GreenfieldAPITests.InvoiceTests`: PASS, 1/1, using BTCPay's regtest test harness. It exercises create/retrieve and the invoice event sequence, including `InvoiceCreated`, `InvoiceReceivedPayment`, `InvoiceProcessing`, `InvoiceSettled`, and invalidation. |
| Actual pinned XBoard `Plugin::pay()` against Greenfield | Blocked before an invoice was returned | With no wallet on the isolated store, Greenfield returned HTTP 400 `No wallet has been linked to your BTCPay Store`. After attaching a test-only regtest BTC wallet, BTCPay returned HTTP 400 `RateUnavailable` / `Unable to get rate BTC_CNY`; the configured CoinGecko source could not supply that conversion. |
| XBoard checkout link / persisted invoice metadata | Not reached | The create request failed with HTTP 400, so no `invoiceId`, `checkoutLink`, retrievable invoice, currency/amount persistence, or invoice webhook could be verified through the XBoard call. |
| Actual provider webhook callback | Not reached end-to-end | No invoice was created by `Plugin::pay()`, so BTCPay had no invoice event to deliver to this provider. The XBoard handler was reviewed against its pinned source; see below. |
| BTCX payment-method mock | Not run | The v2.4.4 test instance had no BTCX payment method. Creating a test extension would only prove a made-up test double, not compatibility of the future BTCX handler, and would exceed this proof's value without a real extension contract. No BTCX blockchain/wallet/listener was added. |

The provider's PHP cURL request did reach the isolated BTCPay API. The harness captured the HTTP status and response body. In both failure cases XBoard surfaced only a generic `error!`, hiding the API's actionable response. The second case is a concrete blocker for the current CNY invoice plus default BTC payment method: BTCPay needs a CNY-to-BTC rate to create the BTC invoice, and the configured provider did not provide it. It does not establish that a future CNY invoice with a BTCX-specific rate/payment handler will fail; that combination does not exist yet and remains unproven.

## What the pinned BTCPay test establishes

The release integration test starts BTCPay and its test dependencies in a regtest environment, authenticates against Greenfield, creates and retrieves invoices, and observes the invoice event lifecycle. This confirms the API and event behavior implemented by v2.4.4 in its own supported test harness. It does **not** establish that the pinned XBoard provider can complete a CNY invoice, that the configured production FX source works, or that BTCX can be registered as a payment method.

BTCPay sends a signed webhook body with the invoice event type and invoice data. A signed `InvoiceCreated` is a normal event, not evidence of payment. A later `InvoiceSettled` is the completed invoice state; `InvoiceProcessing` means payment is still awaiting settlement requirements. A receiver must fetch the invoice and validate its final state and expected order terms before fulfillment. The invoice ID in the event is the identifier to retrieve; its metadata is available on the retrieved invoice. Signature verification authenticates the body as from a party holding the webhook secret, but it does not by itself prove that the order should be fulfilled.

## Pinned XBoard provider behavior

Source reviewed: pinned `plugins-core/Btcpay/Plugin.php`.

- `pay()` takes the XBoard order total, converts minor units to a decimal amount, sets `currency` to `CNY`, and puts the XBoard `trade_no` in Greenfield invoice `metadata.orderId`. It submits the invoice request using the configured Greenfield token, then returns the `checkoutLink` when the response is successful. It does not create/register a BTCPay webhook as part of invoice creation.
- `notify()` reads the raw request body, checks `Btcpay-Sig` using HMAC-SHA256 and the configured secret, extracts the event's `invoiceId`, retrieves that invoice from Greenfield, and returns `metadata.orderId` and the invoice ID to the XBoard payment framework.
- It does **not** check webhook `type`; invoice status; expected invoice ID/order association beyond using the event's ID for retrieval; expected amount or currency; BTCX payment method; expiry; required confirmations; or replay/idempotency state. In particular, the path can treat the signed `InvoiceCreated` event as a successful notification because it does not require a settled invoice state. It also does not robustly handle Greenfield HTTP errors or malformed/missing JSON fields.
- Header lookup expects `Btcpay-Sig` under a particular header spelling/casing. HTTP header names are case-insensitive; production code should normalize names rather than depend on PHP/web-server casing behavior.

## Required XBoard behavior before accepting payments

For a received webhook, validate the raw-body signature with constant-time comparison; parse a known event type; retrieve the invoice from the configured BTCPay host; ensure the fetched ID equals the requested/event invoice ID; bind its `metadata.orderId` to an existing pending order and the expected BTCPay store; compare exact expected currency and amount; require the intended payment method; and only fulfill from the approved final BTCPay state after payment confirmation/settlement policy is met. Enforce expiry and an idempotency key (store + invoice/event identity), and record duplicate deliveries without repeating fulfillment. Partial, late, overpaid, underpaid, invalid, expired, and processing payments need explicit order-state handling. `InvoiceCreated` and `InvoiceReceivedPayment` must not directly fulfill an order.

BTCPay provides authenticated API access, signed webhooks when configured, invoice identity/status, and its own payment-state calculation. XBoard must still validate that these values match the order and merchant policy; Greenfield does not know the XBoard order's expected amount, order identity, or fulfillment rule.

## Blockers and next proof boundary

1. The current BTCPay CoinGecko configuration could not quote `BTC_CNY` in the isolated run, so the ordinary BTC payment method could not create the CNY invoice. A wallet-less store is also not a valid payable-invoice setup.
2. No native BTCX rate source or BTCX payment method was present. The rate-source decision and Observatory valuation distinction are documented in [BTCX rate source audit](btcx-rate-source.md) and [Observatory audit](btcx-observatory-rate.md).
3. Do not interpret the official Greenfield integration test passing as an XBoard end-to-end pass. Actual XBoard invoice creation, checkoutLink, retrieve, event delivery, and signature acceptance remain unverified until a development configuration with a working test rate/payment method is available.
4. Keep BTCX disabled for checkout until a verified native-asset quote source, a BTCX payment method, and the XBoard webhook checks above are implemented and separately validated. This is a compatibility proof only; it does not authorize TASK 02 or production use.
