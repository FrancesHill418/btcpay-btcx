# Historical TASK 02.1 — BTCX runtime integration smoke test

**Historical:** this report predates the completed payment listener and standalone XBoard integration. Its “skeleton” result is not current release evidence. See [staging deployment](staging-deployment.md) for the later standalone-plugin E2E and [release manifest](release-manifest-v0.1.0-rc3.md) for current validation status.

Date: 2026-09-27

## Environment

- BTCPay Server tag: `v2.4.4`
- BTCPay Server source commit: `2d5a0d8077bb33af080e949031da33d84b80638d`
- Runtime reported by BTCPay: `2.4.4+2d5a0d8077bb33af080e949031da33d84b80638d`
- Plugin source commit before this smoke-test change: `80b5b6b`
- Runtime: .NET SDK `10.0.401`; test host reported .NET `10.0.12`
- Database: isolated test PostgreSQL from the pinned BTCPay test compose file, with a unique per-test database
- Chain services: disabled with `BTCPAY_NODEFAULTCHAIN=true`; no Bitcoin, NBXplorer, wallet, or production service was started
- Plugin load: `DEBUG_PLUGINS` pointed to the locally built BTCX DLL

The integration test uses the pinned BTCPay `BTCPayServer.Tests` host, which runs the real `Startup`, `PluginManager`, MVC discovery, dependency injection, PostgreSQL migrations, Greenfield routes, and checkout pipeline. The isolated test only started the PostgreSQL compose service.

Run the regular plugin suite from its in-process runner:

```bash
dotnet restore tests/BTCPayServer.Plugins.BTCX.Tests/BTCPayServer.Plugins.BTCX.Tests.csproj
dotnet build submodules/btcpayserver/BTCPayServer/BTCPayServer.csproj --no-restore -p:StaticWebAssetsEnabled=true
dotnet build tests/BTCPayServer.Plugins.BTCX.Tests/BTCPayServer.Plugins.BTCX.Tests.csproj --no-restore -p:BuildProjectReferences=false
dotnet tests/BTCPayServer.Plugins.BTCX.Tests/bin/Debug/net10.0/BTCPayServer.Plugins.BTCX.Tests.dll
```

Run the host smoke test after starting the isolated `postgres` service from `submodules/btcpayserver/BTCPayServer.Tests/docker-compose.yml`:

```bash
BTCX_RUNTIME_SMOKE=1 dotnet tests/BTCPayServer.Plugins.BTCX.Tests/bin/Debug/net10.0/BTCPayServer.Plugins.BTCX.Tests.dll
```

The runtime smoke test is skipped by default, so normal unit-test runs do not require PostgreSQL or a running BTCPay host. The in-process test runner is invoked from the built test DLL because `dotnet test` in this workspace fails before discovery: the transitive BTCPay test-project build requests static-web-assets manifests that BTCPay's test project disables. No source change was made in the BTCPay submodule to work around this.

## Results

| Check | Result | Evidence |
|---|---|---|
| BTCPay startup | Pass | Host completed database migrations, started HTTP listener, and reported the site operational. |
| PluginManager discovery/load | Pass | Startup log reported `Running plugin BTCPayServer.Plugins.BTCX - 0.1.0`; the runtime `PluginService.LoadedPlugins` collection contained the BTCX entry point. |
| Entry point and DI | Pass | Runtime resolved the BTCX network, payment handler, settings service, checkout extension and `ManualBtcxRateProvider`. |
| Payment method registration | Pass | Runtime handler dictionary contained the chain payment method id `BTCX-OnChain`; BTCX network displayed as `BTCX`, divisibility `8`. |
| Contextual provider registration | Pass | Runtime `RateProviderFactory.Providers["manualbtcx"]` was the plugin's `ManualBtcxRateProvider`, not a background cache wrapper. |
| Immediate rate update | Pass | Runtime query returned `0.20`, then returned `0.25` immediately after updating persisted settings. Disabled provider returned no BTCX/CNY rate while BTCPay continued to serve requests. |
| Settings route and model | Pass with scope | MVC discovered `/server/btcx`; the controller `Index` action returned the current `0.25`, enabled state, `manual` source and `updatedAt`. The `Save` action persisted the manual rate and updated timestamp. This was an in-process action test, not an authenticated browser/form submission. |
| Greenfield invoice A | Pass | Greenfield created a ¥7.50 CNY invoice with BTCX selected, returned invoice ID and checkout link, and retained `metadata.orderId=runtime-A`. |
| Checkout A | Pass with skeleton limitation | Checkout URL returned HTTP 200. Rendered page included BTCX and the expected `37.5` amount at `1 BTCX = 0.20 CNY`. The prompt has no destination/payment link, as expected for the non-payable skeleton. |
| Invoice A snapshot | Pass | Persisted payment prompt retained fiat amount `7.50`, fiat currency `CNY`, BTCX amount `37.5`, rate `0.20`, source `manual`, and rate timestamp. |
| Invoice B snapshot | Pass | After changing the setting to `0.25`, a second ¥7.50 invoice retained `30 BTCX` and rate `0.25`; invoice A still retained `0.20` and `37.5 BTCX`. |
| Invalid/missing configuration | Pass at unit-test level | Existing plugin tests cover disabled, missing, zero, negative and invalid precision values. At runtime, disabling the provider removed its quote without taking down the host. Startup-failure injection was not performed. |
| Existing tests | Pass | Standard in-process runner: 29 existing tests passed, runtime test skipped. Runtime command: all 30 test cases passed, including the host smoke test. Build: 0 warnings, 0 errors. |

## Compatibility finding

The runtime prompt JSON stores `rateTimestamp` as an integer using BTCPay's blob serializer. Calling the BTCX handler's `ParsePaymentPromptDetails` on that persisted prompt threw a `Newtonsoft.Json.JsonReaderException` (`Unexpected token: Integer`, path `rateTimestamp`). This was observed while validating the persisted prompt and was not hidden by the smoke test's raw snapshot assertions.

BTCPay v2.4.4's `GreenfieldInvoiceController.ToPaymentMethodModels` calls `ParsePaymentPromptDetails` when returning activated payment-method details (`submodules/btcpayserver/BTCPayServer/Controllers/GreenField/GreenfieldInvoiceController.cs`). Therefore a Greenfield invoice response that includes payment methods may fail for BTCX until the plugin parser accepts BTCPay's serialized timestamp representation. The basic create-invoice response and checkout page succeeded. No plugin, BTCPay core, or XBoard behavior was changed as part of this validation task.

## Logging and limits

- The BTCPay application log contained routine API-key-cleanup wording, but no API-key value, wallet seed, private key, RPC password, or webhook secret. The development instance had no wallet or RPC credentials configured.
- The generated development account credentials and API key were not emitted by the smoke test output or written into this document.
- The checkout is a skeleton: it proves method registration, conversion, presentation, and immutable invoice snapshots; it cannot accept or settle BTCX payments.
- Browser-based settings form interaction and Greenfield retrieval with `includePaymentMethods=true` were not completed. The parser finding above must be resolved and then exercised in a later, explicitly authorized plugin implementation task.
- No BTCPay core source or XBoard source was modified.
