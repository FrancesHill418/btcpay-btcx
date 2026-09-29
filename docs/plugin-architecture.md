# Historical BTCX BTCPay plugin skeleton design

**Superseded implementation snapshot.** This describes the early non-payable skeleton and is not the current payment-flow or production guide. The implemented BTCX payment behavior and release locks are documented in [README](../README.md), [BTCX implementation map](btcx-implementation-map.md), and [production version matrix](production-version-matrix.md). Do not use this document to infer current runtime capabilities.

**Plugin:** `BTCPayServer.Plugins.BTCX` v0.1.0. **BTCPay target:** v2.4.4, submodule commit `2d5a0d8077bb33af080e949031da33d84b80638d`. This implementation adds the plugin entry, BTCX payment-method skeleton, manual BTCX/CNY rate source, administrator settings page, immutable quote details, and unit tests. It does not implement a BTCX node, wallet, transaction listener, scanner, confirmation processing, or withdrawal.

## Plugin project and loading

- `src/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.csproj` targets `net10.0` and references the pinned BTCPay Server project through `submodules/btcpayserver`.
- `Plugin` extends v2.4.4's `BaseBTCPayServerPlugin`, exposes the plugin identifier/version/dependency (`BTCPayServer >=2.4.4`), and registers services in `Execute(IServiceCollection)`.
- `BTCPayServer.Plugins.BTCX.json` provides the plugin manager manifest. The assembly and manifest identifiers agree.
- The development solution includes the plugin and tests. The official template's development workflow loads a built DLL through `DEBUG_PLUGINS`; installed plugins are loaded by BTCPay's `PluginManager`. This checkout does not change BTCPay's plugin loader or its runtime configuration.

The project layout and .NET 10 target follow the [official plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template/tree/ebc4d8891aa5de03cb54edceda566fe5b3110d46). Payment method registration follows the extension patterns visible in the [current Monero plugin](https://github.com/btcpay-monero/btcpayserver-monero-plugin/tree/9e284e8f26cfdd08c015348b356a32ef6ed7ddd4), but the implemented contract names were checked against the pinned v2.4.4 BTCPay source.

## DI and registration

`Plugin.Execute` registers:

- a `BTCPayNetworkBase` named BTCX, CNY-specific default rule, currency metadata, and divisibility `8`;
- `ManualRateSettingsService` backed by BTCPay's `ISettingsRepository`;
- `ManualBtcxRateProvider` as `IRateProvider` (`IContextualRateProvider`);
- `BtcxPaymentMethodHandler` as `IPaymentMethodHandler`;
- the v2.4.4 `IPaymentLinkExtension` and `ICheckoutModelExtension` contracts;
- a server-settings search link for the BTCX settings controller.

No BTCPay core file was changed. `submodules/btcpayserver` is a clean git submodule pinned to v2.4.4.

## BTCX payment method skeleton

The plugin uses the v2.4.4 `PaymentTypes.CHAIN.GetPaymentMethodId("BTCX")` and `IPaymentMethodHandler`. `BeforeFetchingRates` identifies BTCX as the payment currency and uses the BTCX network divisibility. `ConfigurePrompt` verifies the enabled rate snapshot and writes the quote fields to the payment prompt details. BTCPay persists payment prompt details with the invoice.

The checkout model extension exposes the quote and a `skeleton-not-payable` marker. The payment-link extension returns no address/URI. Therefore this is an architectural prompt/checkout hook only: there is no address to send to and BTCX is not yet payable on-chain. Do not enable it for customer payments until wallet/address generation and payment monitoring exist.

## Manual BTCX/CNY provider

The provider pair is `BTCX_CNY`; one BTCX is worth `X` CNY. For a CNY invoice, BTCPay obtains `BTCX_CNY` and calculates:

```text
BTCX due = CNY invoice amount / BTCX_CNY rate
```

The provider ID is `manualbtcx`, referenced from the BTCX network's default rule. It implements `IContextualRateProvider`. In v2.4.4, `RateProviderFactory` does not wrap contextual providers in the background fetcher; a non-contextual provider would otherwise be refreshed once per minute and considered valid for five minutes. This provider reads the current settings on each contextual rate request. Disabled, missing, invalid, malformed/future-timestamp, or wrong-source settings return no rate; no market or Observatory fallback exists. A valid administrator-set rate does not age out automatically.

Rates are `decimal`, positive, and limited to eight meaningful fractional digits. The source identifier is the literal `manual`. BTCPay's store rate-rule editor may still override the default script, so deployment must verify that the `BTCX_CNY` rule selects `manualbtcx` and does not introduce another BTCX pricing fallback.

## Settings UI and persistence

The server-wide settings page is `/server/btcx`, protected by BTCPay cookie auth and `CanModifyServerSettings`. It provides Enable BTCX, `1 BTCX = [rate] CNY`, source `Manual`, and last-updated timestamp. Values persist through `ISettingsRepository`; no new database or migration was introduced. A missing setting defaults disabled. Saving a valid value sets `updatedAt` from the server's UTC clock. Input accepts invariant decimal notation, rejects zero, negative, non-decimal, and over-precision rates, and logs old/new values and actor.

## Immutable invoice quote snapshot

The `BtcxInvoiceSnapshot` stored in `PaymentPrompt.Details` contains these JSON properties:

| Requested field | Stored member |
|---|---|
| `fiatAmount` | `FiatAmount` |
| `fiatCurrency` | `FiatCurrency` |
| `cryptoAmount` | `CryptoAmount` |
| `cryptoCurrency` | `CryptoCurrency` |
| `exchangeRate` | `ExchangeRate` |
| `rateSource` | `RateSource` (`manual`) |
| `rateTimestamp` | `RateTimestamp` (`updatedAt` of the captured setting) |

Before rate fetch, the handler captures the setting snapshot. Before building payment prompt details, it confirms the setting's rate/timestamp still match that snapshot and confirms BTCPay's persisted invoice rate is the same. If they changed during invoice creation, BTCX prompt creation fails and the caller can retry. The BTCX crypto amount is copied from BTCPay's calculated prompt, not calculated separately for persistence. Later setting edits change only new rate requests; BTCPay does not reprice existing invoice data.

## Amount precision and minimum unit

The skeleton configures 8 decimal places for BTCX. Calculations use `decimal` and BTCPay v2.4.4's `Extensions.RoundUp`, matching `PaymentPrompt.Calculate()` and avoiding an amount rounded below the invoice value. The minimum representable amount is one base unit, `0.00000001 BTCX`. Thus a very small quotient rounds up to that unit. A chain-specific dust threshold is not available until the BTCX transaction/network integration is implemented; this skeleton does not claim the minimum base unit is relayable or economically spendable. Overflowing calculations fail rather than saturate silently.

## Future blockchain integration point

The future wallet/payment work belongs behind the BTCX handler and plugin services: configure a destination in `ConfigurePrompt`, expose the payment URI/link, persist chain-specific prompt data, and use the handler callbacks plus a dedicated listener to discover payments and drive BTCPay invoice state. It must use BTCX chain identity and the already-saved quote/prompt. No RPC, node, wallet, address generation, UTXO, scanner, block listener, confirmation logic, or withdrawal code is present here.

## Validation

The pinned BTCPay v2.4.4 source was compiled as a project reference. Unit tests cover provider values `0.20`/`0.25`, immediate configuration updates through `RateProviderFactory`, disabled/missing/invalid inputs, plugin DI registrations, settings controller save/rejection, calculation precision/round-up/minimum unit/large amount/overflow, and invoice snapshot immutability and fields.

Commands run from the workspace using SDK `10.0.401` at `/usr/share/dotnet/sdk/10.0.401` (Debian 13):

```text
dotnet --info
dotnet restore
dotnet build
dotnet test
```

TASK 02 unit tests cover the plugin entry type and DI registration. TASK 02.1 separately loaded the DLL through the real pinned BTCPay test host and verified PluginManager discovery, Greenfield invoice creation, checkout rendering and invoice snapshots. That checkout has no destination and cannot receive payment; see [runtime-smoke-test.md](runtime-smoke-test.md) for a serialization/parser finding and the remaining validation limits.

## Source references

- [v2.4.4 `BaseBTCPayServerPlugin`](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Abstractions/Models/BaseBTCPayServerPlugin.cs)
- [v2.4.4 `IPaymentMethodHandler`](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Payments/IPaymentMethodHandler.cs)
- [v2.4.4 `ICheckoutModelExtension`](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Payments/ICheckoutModelExtension.cs)
- [v2.4.4 `IPaymentLinkExtension`](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Payments/IPaymentLinkExtension.cs)
- [v2.4.4 `IContextualRateProvider` and rate cache behavior](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Rating/Services/RateProviderFactory.cs)
- [v2.4.4 settings repository](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Abstractions/Contracts/ISettingsRepository.cs)
