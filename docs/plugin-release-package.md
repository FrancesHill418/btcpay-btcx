# BTCX plugin release package

The installable artifact is built from `src/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.csproj` using .NET 10 and the BTCPay v2.4.4 source submodule pinned at `2d5a0d8077bb33af080e949031da33d84b80638d`.

From the repository root:

```sh
./scripts/package-plugin.sh
```

The script runs Release `dotnet publish`, then BTCPay's pinned `BTCPayServer.PluginPacker`. It creates:

```text
artifacts/plugin-package/BTCPayServer.Plugins.BTCX/0.1.0/
  BTCPayServer.Plugins.BTCX.btcpay
  BTCPayServer.Plugins.BTCX.btcpay.json
  SHA256SUMS
```

The `.btcpay` package contains the compiled plugin DLL, dependency manifest, and plugin manifest. The plugin is `BTCPayServer.Plugins.BTCX` v0.1.0, target `net10.0`, with dependency condition `BTCPayServer >=2.4.4`; the manifest identifier and runtime plugin identifier match. The package was generated successfully on 2026-09-29. Current package SHA-256: `4e840c3b8bf7ae4144f049711e56f94a2138fb3256db9157bd69d2cccb0c1453`; package metadata SHA-256: `615cf3c3d112a0a6360412c358c4d21bf61e8552a252137dc9d44365e9f13c64`. Rebuilds may produce different ZIP bytes; record the hash of the exact artifact that is reviewed and deployed.

On a non-production BTCPay v2.4.4 instance, use **Server Settings → Plugins → Upload** to upload the `.btcpay` package, then restart BTCPay when prompted. Verify the loaded identifier/version and payment workflow. Do not use `DEBUG_PLUGINS` for a release or production; official documentation describes it for local development only. Do not install this artifact on a production server before release gates and separate production authorization pass.

The Plugin Builder is the official route for publicly discoverable pre-release/release distribution. It requires a public GitHub/GitLab source repository, runs `dotnet publish` and packages the plugin, but does not run the test suite or prove runtime correctness. This repository has not yet been published/listed there. See [official plugin publishing guidance](https://docs.btcpayserver.org/Development/Plugins/#publishing-the-plugin).
