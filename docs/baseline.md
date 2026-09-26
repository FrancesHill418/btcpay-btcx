# Reproducible development baseline

**Recorded:** 2026-09-26. Upstream sources were fetched into temporary directories under `/tmp/btcpay4btcx-*` for inspection. Those working copies are not project dependencies and are not tracked here. Each source below is pinned by commit SHA; tags/branches are labels, not floating dependency selectors.

## BTCPay

- **Proposed target:** BTCPay Server `v2.4.1`, official Releases page's latest stable release observed during this audit; published 2026-07-23.
- **Tag / commit:** `v2.4.1` / `03345c2886a58ea4d2f603cb6554b2e3f5d690a4`.
- **Target framework:** `net10.0` (`Build/Common.csproj` at the target tag).
- **SDK:** .NET 10 SDK. The checked-out tag does not contain a `global.json` pin, so no exact SDK patch version is imposed by BTCPay source. Plugin template and Monero reference both target `net10.0`; template declares plugin dependency `BTCPayServer >=2.4.0`, which includes the proposed `2.4.1` target.
- **Decision status:** proposed for user confirmation before TASK 02. It is a release-tagged stable version, not `master`.
- **Release evidence:** [official release v2.4.1](https://github.com/btcpayserver/btcpayserver/releases/tag/v2.4.1).

## XBoard

- **Production image:** `ghcr.io/cedar2025/xboard:4f48e61`.
- **Source branch / commit:** `master` / `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` (commit `fix(order): prevent duplicate refunds and redemptions`).
- **Image digest:** GHCR package page reports version `4f48e61` with platform manifests: `linux/amd64 sha256:3a432c8e3fd243f70afd885e3d4898343d0371971007def6c5f8e7b46991c0cf` and `linux/arm64 sha256:3f079eaeb962ec0f9e6650868bd25d47daa594ef983e0e735eb000082a737384`. Use the digest matching deployment architecture; local Docker daemon was not accessible, so the locally deployed manifest/image ID could not be independently inspected. [GHCR package](https://github.com/cedar2025/Xboard/pkgs/container/xboard).
- **Notify route (verified in source):** `app/Http/Routes/V1/GuestRoute.php` registers GET and POST `/payment/notify/{method}/{uuid}` under `/api/v1/guest`; `app/Http/Controllers/V1/Guest/PaymentController.php` calls `PaymentService::notify`, rejects false verification, then calls `handle(trade_no, callback_no)`. `PaymentService` resolves the configured method by payment UUID and invokes that provider's `notify` method.
- **Existing BTCPay provider:** `plugins-core/Btcpay/Plugin.php` already creates a Greenfield invoice through `POST /api/v1/stores/{storeId}/invoices` in CNY, puts XBoard `trade_no` in `metadata.orderId`, verifies `Btcpay-Sig` HMAC, fetches the invoice, and maps its metadata order ID to XBoard's order. `OrderService::paid` row-locks the order and transitions a pending order to processing once, then synchronously dispatches `OrderHandleJob`; repeat callbacks for non-pending orders are no-ops.
- **Security/compatibility finding:** current provider code verifies HMAC but does not validate webhook event type, invoice status, received amount, or currency before returning a valid order mapping. A validly signed non-settlement event could therefore enter the paid path. The payment notify endpoint also accepts GET, though BTCPay sends POST webhooks. No XBoard-specific payment provider tests were found during this source search. Do not use this callback for production fulfillment until status/amount/currency/event validation is addressed and tested.
- **Licensing:** XBoard repository includes MIT `LICENSE`.

## BTCX dependencies

| Repository | Branch at inspected commit | Commit | Tag/release | Purpose and API surface | License at pinned source |
|---|---|---|---|---|---|
| [bitcoin-pocx](https://github.com/PoC-Consortium/bitcoin-pocx) | `master` | `005bf0098e217b76a2627bfae458dff4f5718dd5` | No tag at shallow checkout | Bitcoin Core-derived consensus node; node/wallet RPC and REST. BTCX-specific extended PoCX block/header behavior. | README says it inherits Bitcoin Core MIT; no top-level license file was present at the inspected repository commit. Confirm licensing for any copied/linked component before use. |
| [btcx](https://github.com/PoC-Consortium/btcx) | `master` | `6907bacb132324e460cbe55d3765b4350bb56e61` | `v0.1.1` | Rust workspace with BTCX network/address params, key derivation, seed storage, Electrum client and BDK wallet APIs. | MIT (`Cargo.toml` workspace metadata). |
| [bindex-btcx](https://github.com/PoC-Consortium/bindex-btcx) | `btcx` | `eda7c70660baa06affef464c7ea1e131c39304f1` | No release/tag observed | Rust BTCX block/index library; reads Bitcoin-PoCX Core REST, handles PoCX 286-byte headers and signature-zeroed block hash; used by electrs-btcx. | MIT (`LICENSE`). |
| [electrs-btcx](https://github.com/PoC-Consortium/electrs-btcx) | `btcx` | `2f78c63e20215e20944767f0901209c4d740fe5b` | `v0.11.1-btcx.1` | Electrum protocol v1.4 server; scripthash history/subscriptions and wallet sync; depends on bindex-btcx. | MIT (`LICENSE`). |
| [esplora-pocx](https://github.com/PoC-Consortium/esplora-pocx) | `pocx` | `2b7e1c8a5d2dde2d688974e5bdaf604d223283c8` | No release/tag observed | Esplora HTTP API: address/scripthash history and UTXO, transaction/raw/status, blocks/tip and broadcast endpoints per `API.md`. | MIT (`LICENSE` and upstream flavor license). |

All five are pinned by commit above for traceability. Do not substitute the current branch head without recording and reviewing a new SHA. Indexer APIs are candidates for a plugin adapter, not yet production-validated dependencies.

## BTCPay reference repositories

| Repository | Branch/tag | Commit | Framework / packages | Role |
|---|---|---|---|---|
| [btcpayserver](https://github.com/btcpayserver/btcpayserver) | tag `v2.4.1` | `03345c2886a58ea4d2f603cb6554b2e3f5d690a4` | `net10.0`; SDK family .NET 10; no exact `global.json` patch pin | Proposed compatibility target and extension API source. |
| [btcpayserver-plugin-template](https://github.com/btcpayserver/btcpayserver-plugin-template) | `master` snapshot | `ebc4d8891aa5de03cb54edceda566fe5b3110d46` | `net10.0`; plugin dependency `BTCPayServer >=2.4.0`; no direct package reference in minimal template project | Current structure/metadata/loading reference. Not itself a release-pinned BTCPay runtime. |
| [btcpayserver-monero-plugin](https://github.com/btcpay-monero/btcpayserver-monero-plugin) | `master` snapshot, plugin version `1.3.5` | `9e284e8f26cfdd08c015348b356a32ef6ed7ddd4` | `net10.0`; notable direct runtime package `MoneroNet 1.1.0`; unit/integration tests include xunit.v3 `3.2.2`, Microsoft.NET.Test.Sdk `18.8.1`, Playwright `1.61.0` | Current external altcoin plugin reference; not a BTCX implementation or target to copy without adaptation. |

Template and Monero snapshots are pinned in this document for audit reproduction. Before implementation, compare their `PluginDependency` declarations to the proposed BTCPay target and replace moving branch references with reviewed immutable SHAs/tags in any build metadata.

## Development environment

| Component | Detected |
|---|---|
| OS | Linux Mint 22.3 “Zena”, Ubuntu 24.04 base, x86_64, kernel `6.14.0-37-generic` |
| .NET SDK | Not installed / `dotnet: command not found` |
| Docker CLI | 29.8.1, API 1.56 |
| Docker daemon | Not inspectable: access to `/var/run/docker.sock` returned permission denied, including the read-only container listing attempt |
| Docker Compose | v5.5.1 |
| Git | 2.43.0 |
| BTCPay deployment / installed plugins | **Unverified** because Docker daemon is inaccessible. Do not claim “not deployed” until `docker ps`/image inspection succeeds with an authorized daemon user. |

## Repositories

- https://github.com/btcpayserver/btcpayserver
- https://github.com/btcpayserver/btcpayserver-plugin-template
- https://github.com/btcpay-monero/btcpayserver-monero-plugin
- https://github.com/PoC-Consortium/bitcoin-pocx
- https://github.com/PoC-Consortium/btcx
- https://github.com/PoC-Consortium/bindex-btcx
- https://github.com/PoC-Consortium/electrs-btcx
- https://github.com/PoC-Consortium/esplora-pocx
- https://github.com/cedar2025/Xboard

## Compatibility status

1. **Verified by source inspection only:** BTCPay `v2.4.1` project targets `net10.0`; current template targets `net10.0` and declares BTCPay `>=2.4.0`; Monero plugin targets `net10.0`; BTCX-specific source repositories and XBoard's specified commit were fetched and inspected at the SHAs above.
2. **Reference only, not compatibility proof:** BTCX RPC, wallet, indexer behavior, reorg/restart recovery, plugin load on BTCPay 2.4.1, and plugin method visibility through XBoard's existing BTCPay provider. No services were run and no cross-project builds were possible.
3. **Still requires runtime tests:** BTCX plugin loading against exact BTCPay 2.4.1, invoice payment-method selection, address/script/amount correctness, mempool and confirmation flow, reorg recovery, BTCPay webhook event mapping, XBoard HMAC callback end-to-end and negative event/amount/currency tests, and Docker deployment visibility/digests.

No BTCX functionality has been implemented. **TASK 02 must wait for explicit confirmation of proposed BTCPay target `v2.4.1` and resolution of local .NET/Docker access.**
