# Reproducible development baseline

**Initial audit:** 2026-09-26. **Current baseline synchronized:** 2026-09-28. Upstream sources were fetched into temporary directories under `/tmp/btcpay4btcx-*` for inspection. Those working copies are not project dependencies and are not tracked here. Each source below is pinned by commit SHA; tags/branches are labels, not floating dependency selectors. Current task completion and blockers are summarized in [PROJECT-STATE.md](PROJECT-STATE.md).

## BTCPay

- **Locked target:** BTCPay Server `v2.4.4`.
- **Tag / commit:** `v2.4.4` / `2d5a0d8077bb33af080e949031da33d84b80638d`; verified by the checked-out `submodules/btcpayserver` submodule HEAD and exact tag.
- **Target framework:** `net10.0` (`Build/Common.csproj` at the pinned commit); plugin project also targets `net10.0`.
- **SDK:** repository `global.json` pins .NET SDK `10.0.401` with `latestPatch` roll-forward. BTCPay v2.4.4 itself has no SDK patch pin in its source; its framework target is `net10.0`. Runtime smoke report records .NET runtime `10.0.12`.
- **Decision status:** fixed and used for TASK 02 and TASK 02.1 runtime validation; no floating `master` target.
- **Release/source evidence:** [official release v2.4.4](https://github.com/btcpayserver/btcpayserver/releases/tag/v2.4.4), [pinned source commit](https://github.com/btcpayserver/btcpayserver/tree/2d5a0d8077bb33af080e949031da33d84b80638d).

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
| [btcpayserver](https://github.com/btcpayserver/btcpayserver) | tag `v2.4.4` | `2d5a0d8077bb33af080e949031da33d84b80638d` | `net10.0`; SDK family .NET 10; upstream has no exact `global.json` patch pin | Locked target, compiled project reference, and runtime smoke host. |
| [btcpayserver-plugin-template](https://github.com/btcpayserver/btcpayserver-plugin-template) | `master` snapshot | `ebc4d8891aa5de03cb54edceda566fe5b3110d46` | `net10.0`; plugin dependency `BTCPayServer >=2.4.0`; no direct package reference in minimal template project | Current structure/metadata/loading reference. Not itself a release-pinned BTCPay runtime. |
| [btcpayserver-monero-plugin](https://github.com/btcpay-monero/btcpayserver-monero-plugin) | `master` snapshot, plugin version `1.3.5` | `9e284e8f26cfdd08c015348b356a32ef6ed7ddd4` | `net10.0`; notable direct runtime package `MoneroNet 1.1.0`; unit/integration tests include xunit.v3 `3.2.2`, Microsoft.NET.Test.Sdk `18.8.1`, Playwright `1.61.0` | Current external altcoin plugin reference; not a BTCX implementation or target to copy without adaptation. |

Template and Monero snapshots are pinned in this document for audit reproduction. Their branch labels identify the inspected snapshots; the listed commit SHAs are the reproducible revisions. The implemented plugin declares `BTCPayServer >=2.4.4`.

## Development environment

| Component | Detected |
|---|---|
| Recorded | 2026-09-27, on the dedicated VPS; host currently reports `localhost` |
| OS / kernel | Debian GNU/Linux 13.7 (trixie), x86_64, kernel `6.12.107+deb13-cloud-amd64` |
| User / capacity | `root`; 5 vCPU, 9.7 GiB RAM (7.6 GiB available at check), 98 GiB root filesystem (90 GiB available); no swap configured |
| Git | 2.47.3 |
| Docker CLI / Compose | Docker 29.8.1, Compose v5.5.1 |
| Docker daemon | Healthy and accessible as root; storage driver `overlayfs`, cgroup v2; service active and enabled at boot |
| Containers | No running containers at check time |
| Node.js / npm | Node.js v22.23.3, npm 10.9.9 |
| .NET | SDK 10.0.401 (repository `global.json` pins `10.0.401`, `latestPatch`); ASP.NET Core and .NET runtimes 10.0.12; `/usr/bin/dotnet` |
| Rust / native build prerequisites | Debian `rustc` and Cargo 1.85.1, `build-essential`, and `pkg-config` installed for BTCX Rust components |
| Utilities | `curl`, `jq`, `openssl`, and Python 3 available |
| Repository | Clean at start of TASK 01.7; no compose file or application package manifest is present yet |

The host and toolchain were checked directly with the TASK 01.7 command list. Rust/Cargo and native build prerequisites were installed from Debian 13 packages during TASK 01.7. TASK 02.1 subsequently ran the pinned BTCPay test host and an isolated PostgreSQL service for runtime plugin smoke tests. No persistent production stack, XBoard deployment, BTCX node, or mainnet wallet was deployed.

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

1. **Runtime-verified:** BTCX plugin loads under the pinned BTCPay v2.4.4 test host; PluginManager/DI/payment method/manual contextual rate provider/settings route were discovered; CNY invoice checkout and immutable rate snapshots were exercised. Details and limits are in [runtime-smoke-test.md](runtime-smoke-test.md).
2. **Still unverified:** address generation, BTCX URI/QR, chain payment detection, mempool/confirmation/reorg/restart handling, final settlement, Phoenix runtime round trip, and XBoard-to-BTCPay webhook end-to-end/security-negative tests.
3. **Deployment boundary:** no persistent production stack, XBoard instance, BTCX node, or mainnet wallet was deployed. See [PROJECT-STATE.md](PROJECT-STATE.md) for current blockers and next task.

The earlier v2.4.1 proposal and its “wait for TASK 02” status are superseded by the locked v2.4.4 implementation and TASK 02.1 runtime validation.
