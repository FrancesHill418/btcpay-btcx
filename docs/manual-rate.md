# TASK 01.6.3 — Manual BTCX/CNY rate architecture

**Target:** BTCPay Server v2.4.4, commit [`2d5a0d8077bb33af080e949031da33d84b80638d`](https://github.com/btcpayserver/btcpayserver/tree/2d5a0d8077bb33af080e949031da33d84b80638d). This TASK 01.6.3 design audit preceded TASK 02's plugin implementation. The implementation and current validation are described in [plugin architecture](plugin-architecture.md).

## Decision

Store one administrator-managed server setting defining **CNY per BTCX**:

```text
1 BTCX = X CNY
BTCX amount = CNY invoice amount / X
```

For example, at `0.225 CNY/BTCX`, a `7.50 CNY` order requires `7.50 / 0.225 = 33.333333333… BTCX` before payment-method precision is applied. There is no market lookup or Observatory dependency in this first version.

The plugin should register a `ManualBtcxRateProvider` for `BTCX_CNY`. It reads the current setting at rate-fetch time, validates it, and returns no rate if disabled or invalid. BTCPay then computes the BTCX payment prompt during invoice creation and persists the invoice's rate/payment prompt. Later configuration changes are only inputs to later invoices; they must never update an already-created `InvoiceEntity` or its payment prompt.

## BTCPay v2.4.4 extension point

The pinned source confirms:

- `BTCPayServer.Rating.Providers.IRateProvider` exposes `RateSourceInfo` and `GetRatesAsync(CancellationToken)`, returning `PairRate[]`. `PairRate` contains a `CurrencyPair` and `BidAsk`; it has no timestamp field.
- `IContextualRateProvider` adds `GetRatesAsync(IRateContext, CancellationToken)`. `RateProviderFactory.InitExchanges()` leaves contextual providers unwrapped. A normal `IRateProvider` is wrapped in `BackgroundFetcherRateProvider` with a one-minute refresh and five-minute validity. That cache is undesirable for admin-editable fixed rates: new invoices could temporarily use the previous value after an update. Implement the contextual interface so invoice rate fetches read the latest persisted setting rather than that background cache.
- A plugin can contribute services through `BaseBTCPayServerPlugin.Execute(IServiceCollection)`. BTCPay's `BTCPayServerServices.AddRateProvider<T>()` is the built-in registration pattern; the plugin can register its provider as `IRateProvider` in its own `Execute` method without modifying core.
- The payment method must itself request the `BTCX_CNY` rate, and the store's rate rule must select the provider's rate source ID. Registering a rate provider alone does not add BTCX to BTCPay's currencies/payment methods or create a BTCX payment method.
- `UIInvoiceController.CreateInvoiceCoreRaw(CreateInvoiceRequest, StoreData, ..., Action<InvoiceEntity> entityManipulator)` exposes a plugin-callable invoice creation path with a post-calculation entity callback. BTCPay's Greenfield `CreateInvoice` path uses the same invoice core flow. The invoice entity persists the calculated `Rates` and payment prompts; its `Metadata` supports additional JSON fields.

### Proposed provider contract

```text
Provider ID: ManualBtcxRateProvider
Pair:        BTCX_CNY
Bid = Ask =  configured btcxCnyRate (CNY for 1 BTCX)
Source:      manual
```

`GetRatesAsync(context, cancellationToken)` reads one immutable settings snapshot `{ enabled, btcxCnyRate, updatedAt }`. It returns one `PairRate(BTCX_CNY, BidAsk(rate, rate))` only if all required values are valid. If disabled, unset, zero, negative, malformed, or missing `updatedAt`, return no BTCX/CNY rate (or a provider error surfaced as a missing rate); never substitute a built-in exchange or a stale cached value. The rate is expressed in the same direction as the pair: one BTCX is worth `X` CNY. The payment amount is therefore invoice CNY divided by that rate.

The provider ID is a rate-rule identifier and `RateSourceInfo.DisplayName` should clearly identify it as manual. Store configuration/rate rules must be tested to ensure a BTCX payment request resolves this provider and no other fallback can silently price BTCX.

## Configuration and administrator UI

Use a persisted plugin setting, not a constant or environment-only value:

```json
{
  "enabled": true,
  "btcxCnyRate": "0.225",
  "updatedAt": "2026-09-27T00:00:00Z"
}
```

Store decimal values as invariant-culture decimal strings or JSON numbers parsed as `decimal`; never pass through binary floating point. The server-side setting should use BTCPay's `ISettingsRepository.GetSettingAsync<T>()` / `UpdateSetting<T>()`. On every accepted save, atomically persist the new positive rate and set `updatedAt` from the BTCPay server's UTC clock. Disabling the rate also updates the setting timestamp. A missing setting stays disabled; do not seed a production price in code. Deployment may require an administrator to enter the initial value before BTCX invoices are enabled.

A plugin settings page is supported by the v2.4.4 plugin/controller patterns. Add a plugin MVC controller and view, register a discoverable settings/search route with `AddStaticSearch`, and protect view/update operations with cookie authentication and the server settings policy (`Policies.CanModifyServerSettings` for changes). The UI should display:

```text
BTCX/CNY rate
1 BTCX = [ X ] CNY
Enabled [ ]
Updated: [UTC timestamp]
```

Validate input on the server: required when enabled, finite decimal, strictly greater than zero, and within the chosen operational bounds. Keep decimal separators invariant in persisted data. Return validation errors without changing the last valid setting. Audit actor, old/new value, timestamp, and enable/disable action. This UI is feasible in a plugin; it is not a BTCPay core change.

## Immutable invoice quote snapshot

The BTCPay invoice rate and payment prompt are fixed during invoice creation and serialized with that invoice. Updating the global setting only supplies a different rate to future rate fetches. However, `PairRate` has no `updatedAt`, and the generic invoice's calculated rate alone does not preserve the administrator-setting timestamp. Therefore, to satisfy the complete audit requirement, the BTCX plugin's invoice-creation integration must persist a quote snapshot in that same invoice's additional metadata **after the payment prompt has been calculated and before the invoice is saved**:

```json
{
  "btcxQuote": {
    "rate": "0.225",
    "rateSource": "manual",
    "rateTimestamp": "2026-09-27T00:00:00Z",
    "cnyAmount": "7.50",
    "expectedBtcxAmount": "33.33333334"
  }
}
```

`cnyAmount` is the final CNY invoice amount after BTCPay's CNY precision normalization. `expectedBtcxAmount` must equal the BTCX payment prompt amount actually saved by BTCPay (including BTCPay's configured payment-method divisibility/round-up behavior), not an independently rounded display-only calculation. Record the original unrounded quotient only as optional audit metadata if useful. Keep the XBoard `metadata.orderId` field intact when merging quote metadata.

Do not implement this as a client-side “get current rate, then create a normal invoice” sequence: an admin could update the rate between the two requests, making the recorded rate differ from the rate BTCPay actually used. The plugin-owned invoice creation path must capture one immutable `{rate, updatedAt}` setting snapshot for the rate calculation and persist that same snapshot in the invoice metadata. The custom provider and creation path need a request-scoped snapshot handoff (safe for concurrent invoice creation), or another verified mechanism that ties the provider result to the entity callback. Do not use mutable process-global “last rate” state. If this handoff cannot be proven, fail the invoice creation rather than write an inaccurate timestamp/expected amount.

Once saved, the invoice's effective quote is immutable. A settings edit creates a new setting version/timestamp but does not revisit old invoices, metadata, exchange rates, payment prompts, expiry, or checkout display.

## Amount precision and rounding

- BTCPay v2.4.4 normalizes fiat invoice amounts using that currency's configured decimal digits. CNY uses two digits.
- Calculate with `decimal` arithmetic, not `double`/`float`.
- Use the BTCX payment method/network's configured divisibility. This audit did not register a BTCX network, so the final BTCX precision must be confirmed in that plugin. Do not hard-code a precision in the rate provider.
- BTCPay v2.4.4 `InvoiceEntity.PaymentPrompt.Calculate()` uses `BTCPayServer.Extensions.RoundUp` for the amount due. This avoids rounding the required on-chain amount down. The persisted `expectedBtcxAmount` should copy the resulting payment prompt's amount.
- With an illustrative eight-decimal BTCX divisibility, `7.50 / 0.225` becomes `33.33333334 BTCX` after round-up. If the BTCX network declares a different divisibility, BTCPay's actual prompt precision takes precedence. A six-decimal presentation would show `33.333334 BTCX`; truncating to `33.333333` is not the amount to require if it underpays the CNY invoice.

## Required behavior for updates and invalid settings

| Setting/change | New invoice | Existing invoice |
|---|---|---|
| Rate `0.20` | At ¥7.50, calculated due is 37.5 BTCX before network precision | Snapshot remains as originally created |
| Change to `0.25` | At ¥7.50, calculated due is 30 BTCX before network precision | Still uses original rate, CNY amount, BTCX prompt, timestamp and metadata |
| Disabled / missing / zero / negative / malformed | BTCX rate unavailable; invoice creation must not offer or accept BTCX | Remains payable under its saved original terms until its configured expiry/policy |
| Valid new rate saved | New timestamp and rate apply to later invoices | No mutation |

Do not clamp invalid values to a minimum, retain an expired previous value, or allow a generic BTCPay rate fallback to create a BTCX amount.

## Validation performed

The BTCPay extension point and invoice calculation behavior above were verified in the pinned v2.4.4 source. A temporary Python `Decimal` reference-model check (not committed as application code or a plugin unit test) exercised these cases; all assertions passed:

| Case | Result |
|---|---|
| Order creation reads configured rate `0.20` | ¥7.50 → 37.50000000 BTCX at illustrative 8-digit precision |
| Rate `0.25` | ¥7.50 → 30.00000000 BTCX |
| Change `0.20` → `0.25` | New quote uses `0.25`; an earlier immutable snapshot remains `0.20` / 37.50000000 |
| Rate `0` | Rejected |
| Rate `< 0` | Rejected |
| Rate not configured | Rejected |
| Missing `updatedAt` | Rejected |
| Decimal precision (`0.225`) | ¥7.50 → raw quotient `33.333…`; due rounded up to 33.33333334 at 8 digits |
| Rounding edge (`¥0.01 / 3`) | 0.00333334 BTCX at 8 digits, rounded upward |
| CNY amount retention | Snapshot preserves normalized ¥7.50 |

These were design-stage reference-model checks only. TASK 02 adds plugin unit tests for provider registration, settings saves, calculations, and saved quote snapshots. A full BTCPay web-server startup and actual Greenfield checkout still require an isolated runtime integration proof; see [plugin architecture](plugin-architecture.md).

## Source references

- [BTCPay v2.4.4 `IRateProvider` and `IContextualRateProvider`](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Rating/Providers/IRateProvider.cs)
- [BTCPay v2.4.4 `RateProviderFactory`](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Rating/Services/RateProviderFactory.cs)
- [BTCPay v2.4.4 rate-provider registration](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Hosting/BTCPayServerServices.cs)
- [BTCPay v2.4.4 invoice creation and entity manipulator](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Controllers/UIInvoiceController.cs)
- [BTCPay v2.4.4 payment prompt amount calculation](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Services/Invoices/InvoiceEntity.cs)
- [BTCPay v2.4.4 settings repository contract](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Abstractions/Contracts/ISettingsRepository.cs)
