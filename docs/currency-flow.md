# CNY and BTCX invoice currency flow

**Target:** BTCPay Server `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d`; XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`. This is a design record, not an implementation.

## BTCPay currency and rate behavior

In v2.4.4, `BTCPayServer.Rating/Currencies.json` registers CNY as a fiat currency with divisibility 2. Store default currency is selected from BTCPay's currency data, and Greenfield invoice creation accepts `currency` per invoice. `UIInvoiceController.CreateInvoiceCoreRaw()` chooses the invoice currency, rounds its display/price precision, asks active payment-method handlers for required currency pairs, evaluates the store's rate rules with `RateFetcher`, and persists the invoice/payment prompts. The resulting amount/rate belongs to that invoice; later rate changes do not recalculate an already-created invoice.

BTCX is not in BTCPay v2.4.4's built-in currency table or standard rate feeds. The built-in `IRateProvider` implementations expose their own `PairRate[]` data; the default on-chain network pattern uses `BTCPayNetwork.DefaultRateRules`. The current standard sources do not provide a BTCX market quote. Adding a source through a plugin is possible at the architectural level (`IRateProvider`, registered into DI with `AddRateProvider<T>()`), but this must be compiled and runtime-verified against the pinned release before relying on it.

## Option A: CNY invoice, BTCX as payment method

```text
XBoard order amount in CNY cents
        ↓ amount = cents / 100, currency = CNY
BTCPay CNY invoice (metadata.orderId = XBoard trade_no)
        ↓ frozen CNY → BTCX rate and BTCX due amount
BTCX checkout prompt/address and invoice expiry
```

**Feasibility:** API/data-model path is present. CNY is supported; Greenfield allows a registered payment method to be selected in `checkout.paymentMethods`, and BTCPay calculates payment prompts using rate rules. However, actual BTCX/CNY conversion is blocked until a plugin provides a reliable BTCX rate and BTCX payment handler. XBoard currently omits `checkout.paymentMethods`, so the BTCX method must be active in store configuration until XBoard explicitly selects it.

Advantages: XBoard remains the CNY order ledger; coupons, balance, discounts and XBoard accounting keep their current currency; BTCPay owns fiat conversion, BTCX due calculation, invoice expiry, payment tracking and settlement; the checkout invoice keeps its CNY amount and BTCX quote together.

Costs: BTCX plugin must provide `BTCX_CNY` or an equivalent deterministic rate rule, with the requested pair available to BTCPay before prompt creation. It must expose CNY and BTCX precision correctly, and BTCPay must accept the custom payment method in the invoice's selected methods. An unavailable/invalid rate must fail invoice creation rather than silently omit BTCX.

## Option B: BTCX-denominated BTCPay invoice

```text
XBoard CNY order
        ↓ convert and round once in a defined rate service
BTCX amount
        ↓ amount = BTCX, currency = BTCX
BTCPay BTCX invoice / BTCX checkout
```

This avoids BTCPay converting the CNY invoice amount at checkout; the invoice amount is already BTCX. BTCPay still owns payment tracking, checkout and invoice status. The plugin must register BTCX as a currency and handler; no external BTCX-to-BTCX conversion is needed after that.

The conversion has to happen before invoice creation, so either XBoard's provider must call a trusted BTCX rate service or an added XBoard backend/service must return the BTCX amount. Current `Order` pricing and `OrderService::paid()` are built around integer minor-unit CNY, while `Plugin::pay()` derives the invoice amount from that field. The amount, rate, timestamp and CNY source total would need durable linkage to the XBoard order; passing a floating-point amount in metadata alone is not a safe record. BTCPay invoice expiration does not refresh the price. If the rate changes, the payment amount remains the quoted BTCX amount, and after expiry the late-payment/partial-payment policy must be explicit.

## Comparison

| Factor | A: CNY invoice + BTCX method | B: BTCX invoice |
|---|---|---|
| BTCPay rate work | BTCX plugin must make BTCPay's BTCX/CNY rate available; invoice prompt locks it | BTCPay does not convert CNY to BTCX; XBoard side must calculate/lock before create |
| XBoard changes | Mainly provider request and secure webhook validation; keep CNY order amount | Provider must fetch/calculate BTCX and record amount/rate/time; may need schema/service changes |
| BTCX plugin changes | Handler, chain monitor, custom rate provider, BTCX currency/network metadata and checkout UI | Handler, chain monitor, BTCX currency/network metadata and checkout UI; rate provider optional for invoice conversion |
| Expiry | BTCPay locks BTCX due amount for this CNY invoice through its set expiry | BTCX amount remains fixed in invoice; source rate is owned by XBoard and must be recorded there |
| Checkout experience | Shows CNY order total plus BTCX amount and BTCPay's remaining/received/confirmation status | Shows BTCX-denominated price; CNY reference is not native invoice amount unless separately displayed |
| Webhook | Same Greenfield invoice webhook; validate retrieved invoice currency `CNY` and BTCX payment prompt/payment method | Same Greenfield webhook; validate invoice currency `BTCX` and expected BTCX amount |
| Maintenance | One conversion/rate implementation owned inside BTCPay; XBoard stays a checkout adapter | Duplicated pricing lifecycle and reconciliation across XBoard and BTCPay; schema and rounding drift risk |
| Extension | Add other fiat currencies in BTCPay rate rules without moving fiat order accounting to XBoard provider | Useful only if XBoard intentionally owns crypto quotes or needs a crypto-native catalog/ledger |

**Recommendation:** use Option A, CNY invoice plus BTCX payment method, after the custom rate source and handler work. It follows the existing XBoard architecture and leaves rate locking, displayed BTCX due, expiration, and payment state within BTCPay. Option B is viable technically, but shifts conversion, quote persistence, and reconciliation into XBoard and is not recommended for this integration.

## Rate architecture recommendation

Implement a BTCX plugin-owned BTCPay `IRateProvider` (or a plugin-owned BTCX pricing component integrated with BTCPay's rate rules) rather than expecting a stock BTCPay provider to know BTCX. It should expose reviewed BTCX pairs needed by configured stores, at minimum BTCX/CNY for Option A. If BTCX/USD or BTCX/USDT is used as a source, the conversion path to invoice currency must be explicit and must not silently assume USDT equals USD or CNY.

The interface design only; no provider is implemented here:

```text
IBtcxQuoteSource.GetQuote(baseCurrency, quoteCurrency, observedAt, cancellationToken)
  → price, base/quote, source/venue, observedAt, receivedAt, sourceSequence/reference

BTCX plugin IRateProvider.GetRatesAsync()
  → PairRate(BTCX/CNY, validated BidAsk)
  → BTCPay rate rules calculate payment prompt and persist invoice rate
```

### Quote and failure policy

- **Rate source:** choose an auditable BTCX market/exchange with an actual BTCX/CNY pair if available. Otherwise choose an explicit BTCX/USDT or BTCX/USD market and a separately approved USDT/CNY or USD/CNY conversion source. Record venue, pair, side, fees/spread policy, and whether the result is executable or indicative. This audit does not select a venue.
- **Timestamp:** preserve the source observation timestamp and ingestion timestamp. Reject future timestamps beyond a small configured clock-skew window.
- **Invoice price lock:** calculate the final rate and integer BTCX base-unit due amount before BTCPay invoice creation completes; BTCPay then persists the invoice amount/rate and expiry. No post-create market update may change the BTCX due amount.
- **Expiry:** use the store/requested invoice expiry. Do not extend a quote without creating/reissuing an invoice and recording the new price. Late payments follow BTCPay's actual invoice state and a documented merchant policy.
- **Stale price:** enforce a short maximum quote age appropriate to the venue's publication interval. Stale quote means BTCX method unavailable and invoice creation fails if BTCX was specifically required; never fall back to zero or a previous unbounded cached rate.
- **Provider failure:** bounded retries/timeouts; if no valid fresh quote, fail closed and return an actionable error. Do not silently use another market whose conversion semantics differ.
- **Abnormal price:** validate positive finite decimal, correct pair orientation, allowed min/max, market deviation versus recent robust reference, spread, and divisibility. Reject large outliers and alert. Use decimal/integer base units, never binary floating point for due amounts.
- **Audit:** persist provider/source, pair, rate, observedAt, quote ID, applied spread, rounded BTCX amount, invoice ID, and expiry for later reconciliation.

## Remaining validation

1. Implement only after approval: BTCX plugin handler, custom rate interface/provider, and store configuration.
2. In isolated test infrastructure, verify CNY invoice creates a BTCX payment prompt with the expected rounded BTCX amount, that the rate is immutable after creation, and that expiration/late payment uses the defined behavior.
3. Test missing, stale, malformed, inverted, zero, negative, outlier and unavailable rates; each must fail closed.
4. Verify separately whether BTCX's PoCX node/indexer can safely supply canonical payment observations to BTCPay without relying on the stock Bitcoin/NBXplorer listener.

Pinned BTCPay references: [invoice controller](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Controllers/GreenField/GreenfieldInvoiceController.cs), [invoice creation and rate-lock path](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Controllers/UIInvoiceController.cs), [rate interface](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Rating/Providers/IRateProvider.cs), [currency table](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Rating/Currencies.json), [rate DI registration](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Hosting/BTCPayServerServices.cs).
