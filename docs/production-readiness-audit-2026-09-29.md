# BTCX production deployment preparation audit

**Audit date:** 2026-09-29
**Result:** **NOT READY / NO-GO**. This is a source and deployment-package audit only. No production server was accessed, no production configuration was applied, no BTCX mainnet RPC was contacted, and `BTCX:Wallet:AllowMainnet` remains default-off.

## Release-hardening progress (2026-09-29)

The repository follow-up created a local `v0.1.0-rc3` candidate at `a0a0cd78c6410fd3bcf64d996366fa480213402d` (never pushed), freezes the requested component commits, verifies each requested upstream SHA, centralizes the required Bitcoin-PoCX REST compatibility patches, pins the BTCPay helper image digests that were available, adds locked NuGet restore inputs, a fail-closed production Compose template, release CI and backup/restore runbook. See [production-version-matrix.md](production-version-matrix.md) and [release-manifest-v0.1.0-rc3.md](release-manifest-v0.1.0-rc3.md) for final tag and artifact evidence.

The architecture and hardening commits are included in the local RC3 candidate; `origin/main` and remote tags remain unchanged. The former patch-artifact finding is closed as a source-layout issue, but the patch itself still requires independent review/upstream disposition. Runtime application images have not yet been built/published in the release workflow, so there are no custom registry digests, release SBOM/provenance artifacts or vulnerability review results. Local package preparation exposed a moderate advisory against the pinned BTCPay build graph's `Microsoft.Build.Tasks.Git 8.0.0` dependency (CVE-2026-62900). The release build now overrides that build-only dependency to patched `10.0.303` from this repository's `Directory.Build.targets`, locks the version/hash, and locked restore/build no longer emits NU1902; no BTCPay core source was changed and the `.btcpay` package does not contain the build task. The pinned BTCPay `BTCPAY_UPDATEURL` is admin update-notification metadata only; code inspection confirmed it reads a GitHub release tag for display and does not fetch or install a runtime image. Helper images in the checked-in generated snapshot now use real digest-qualified refs. Production-host, custody/restore drill, ingress/DNS/secrets, final-image regtest/SBOM vulnerability review, real-device Phoenix compatibility decision and named operational approvals remain open.

## Frozen source and release decision

| Item | Pinned value / finding |
|---|---|
| `btcpay-btcx` application source candidate | `957b8a721b8101ab8260cbf74e6d23eb5eefbfa0`; RC3 tag target includes release-hardening commits listed in the release manifest |
| `origin/main` | `c3a9861e2db03c047fd9e41e02e19d23d6a260da`; local `main` is four commits ahead and has not been published |
| Existing tag | Annotated tag object `f5d6449453713144d093bc8259f3b0c3e0cd78c2`, peeled commit `2925af8b19174ad24418529407a4a408cfdf5e59`, before the standalone XBoard plugin commits; **do not use as the production release** |
| XBoard target | `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` |
| BTCPay Server | `v2.4.4`, source commit `2d5a0d8077bb33af080e949031da33d84b80638d`; base image index `sha256:c264aa08cd32a469bd30d41978b73dc8bb2de1503ce67bdb0ab8fd5d934fb614` |
| BTCPay BTCX plugin | package version `0.1.0`; source candidate is the repository commit above; no published production package checksum/attestation |
| Bitcoin-PoCX | commit `005bf0098e217b76a2627bfae458dff4f5718dd5`; bundled Bitcoin source `b88b852644f629cd5f25b3424d11b462462c24b3` |
| electrs-btcx | `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` |
| bindex-btcx | commit `eda7c70660baa06affef464c7ea1e131c39304f1`, built into electrs; not a service |
| Phoenix PoCX | `v2.4.0`, commit `bc4713306c9c2cd3cbf989a3e355e0705b485218`; client compatibility reference only; real-device E2E is not verified |

**Release choice:** no production release is approved. RC3 is a local non-production candidate for build/test; its exact tag target is recorded in the release manifest. Do not promote `v0.1.0-rc2`.

## XBoard boundary and documentation

`integrations/xboard/BtcpayBtcx/` contains `Plugin.php`, `config.json`, `README.md`, `database/migrations/`, and `Tests/`. It installs as `XBoard/plugins/BtcpayBtcx/` and registers `BTCPayBTCX`. The original provider registers `BTCPay`.

The XBoard source is not in this repository, so the diff command below has no local path to compare. The standalone plugin was previously checked against the exact pinned XBoard checkout; its original `plugins-core/Btcpay/Plugin.php` and `config.json` matched upstream byte-for-byte. No XBoard Core or original-provider source is included/modified here. The historical patch artifact was deleted; surviving patch references are explicitly historical/deprecated or refer to the Bitcoin-PoCX REST compatibility backports. Current installation docs direct users to the standalone plugin.

The plugin migration owns only:

* `btcpay_btcx_invoice_bindings`
* `btcpay_btcx_webhook_registrations`
* `btcpay_btcx_webhook_deliveries`

It does not modify the original BTCPay provider's tables. Disable the plugin independently; do not run its destructive `down()` migration as an ordinary application rollback while BTCX invoice history must be retained.

## Production readiness blockers

1. Current source is not on `origin/main`, and the existing RC tag predates the standalone-plugin architecture.
2. No production Bitcoin-PoCX image was completed; custom images have no published immutable registry manifest digests, provenance, SBOM/signatures or vulnerability review. The BTCPay/electrs local image IDs are not deployable registry locks.
3. The two PoCX v30 REST compatibility patches are required by the selected bindex/electrs and are only development compatibility backports. They are not independently production-reviewed or upstream-merged. Their hashes and exact base are in `image-lock.md`.
4. The checked-in generated BTCPay snapshot now pins helper images by real registry digest. `BTCPAY_UPDATEURL` still names `/releases/latest`, but this is admin-only update-notification metadata and does not alter runtime images; preserve upstream behavior. The selected operator ingress helper images and final generated stack still need review and real-host validation.
5. No production host, DNS, ingress choice, resource profile, secret manager, or storage plan was supplied. The current shell reports `hostname=localhost`, Debian 13, Docker 29.8.1/Compose 5.5.1, 9.7 GiB RAM, no swap, 98 GiB root filesystem, and unsynchronized system time. This is a development workspace/container, **not evidence about the production host**. Its values must not be copied as production requirements or configuration.
6. No production wallet exists, and production encrypted/offline backup and restore have not been rehearsed. This is a hard custody blocker. Existing development/regtest restore evidence does not close it.
7. Phoenix real-device test is pending. The release owner must explicitly accept that gap or close it before production approval.
8. Final-image production-like regtest E2E, production secret mounts/rotation, BTCPay/XBoard database recovery, DNS/TLS/tunnel behavior, reboot recovery, and named security/operations/finance approvals remain unverified.
9. The current generated stack publishes web ingress through nginx on host 80/443. Whether production will use Cloudflare Tunnel or an external reverse proxy is not specified. Choose and test one ingress design; do not add an unreviewed tunnel container. Ensure only the approved HTTPS ingress is reachable and no RPC, Electrum or database port is host-published.

## Intended production topology

```text
Payer/browser ── HTTPS ── approved Cloudflare Tunnel or reverse proxy ── XBoard
                                                      └─────────────── BTCPay Server v2.4.4
BTCPay BTCX plugin ── private RPC/cookie ── Bitcoin-PoCX wallet/node
BTCPay BTCX plugin ── private Electrum ──── electrs-btcx (bindex built in)
BTCPay ── private database network ── PostgreSQL
XBoard BtcpayBtcx ── HTTPS Greenfield/webhook ── BTCPay
```

Phoenix stays a payer-side wallet, not a server container. bindex-btcx stays built into electrs, not a separate service. Bitcoin-PoCX supplies wallet RPC. The current electrs build uses Bitcoin REST on localhost, so its container shares the node network namespace; changing that requires a coordinated compatibility/retest. Node RPC/REST, Electrum and PostgreSQL must have no host port mappings.

## Production directory and secret-file plan

Use a new, operator-owned path; do not copy staging volumes or `.env` values:

```text
/srv/btcpay-btcx/
  release/       source commit, tag, image digests, SBOM, signatures, plugin checksum
  compose/       reviewed generator checkout/fork, source overlay, final generated compose
  config/        sanitized network-specific environment/config (no secret values)
  secrets/       managed secret delivery only; host permissions 0700, files 0600
  backups/       encrypted database/config exports only; wallet backups separately offline
```

The `BtcpayBtcx` config paths are `/run/secrets/btcpay-btcx-api-key` and `/run/secrets/btcpay-btcx-webhook-key`. Mount separate store-scoped Greenfield token and HMAC files read-only; validate effective owner/readability and mode `0600` or stricter compatible with the container runtime. Also protect PostgreSQL credentials. Bitcoin-PoCX generates its RPC cookie in a private volume; electrs gets a distinct restricted `rpcauth` identity. Never export either credential, wallet seed, private key, or secret content into Git, image layers, shell arguments, logs, or this audit.

## Compose and deployment sequence (future authorized change only)

The repository's root `docker-compose.yml` and `integrations/production/.env.example` are development/regtest material, not production deployment inputs. Do not source/copy them unchanged. Production network values must be reviewed together (`NBITCOIN_NETWORK=mainnet`, `BTCX_NETWORK=main`, `BTCX_WALLET_NETWORK=main`, `BTCX_ELECTRS_NETWORK=bitcoin`); `BTCX_ALLOW_MAINNET=false` remains mandatory through readiness verification. Confirm Postgres database name/network, RPC ports, DNS, callback URLs, and wallet are all aligned before generating Compose.

On a controlled build host, after release approval and a clean source checkout:

```sh
git clone https://github.com/FrancesHill418/btcpay-btcx.git /srv/btcpay-btcx/source
git -C /srv/btcpay-btcx/source checkout --detach <approved-new-release-commit>
git -C /srv/btcpay-btcx/source submodule update --init --recursive
git -C /srv/btcpay-btcx/source status --short
```

Build only from reviewed sources in isolated CI; publish to the approved registry; inspect the registry manifest and record `image@sha256:<digest>` for every runtime and helper image. Pin generator source, SDK, OS packages, NuGet/Cargo/PHP dependencies, patches, plugin artifact and XBoard commit. Build `.btcpay` with the locked SDK and record SHA-256. The currently checked-in `example.invalid` references are deliberately unusable.

Before any production install/change, take and restore-verify BTCPay PostgreSQL/data/config, XBoard database/config, wallet encrypted/offline backup, secret-manager recovery and the current deployment lock. Install the XBoard plugin to `XBoard/plugins/BtcpayBtcx/`, install/enable it with plugin code `btcpay_btcx`, and confirm `BTCPay` and `BTCPayBTCX` coexist. Configure its own secret-file paths and production HTTPS endpoints. Do not change original BTCPay config or tables.

Only after a separately approved deployment window may an operator use the reviewed generated file:

```sh
cd /srv/btcpay-btcx/compose
docker compose -f Generated/docker-compose.generated.yml config --quiet
docker compose -f Generated/docker-compose.generated.yml pull
docker compose -f Generated/docker-compose.generated.yml up -d
docker compose -f Generated/docker-compose.generated.yml ps
```

These commands have **not** been run for production. Stop before enabling BTCX payment acceptance. Verify BTCPay, PostgreSQL, node RPC/REST and expected genesis, electrs Electrum/index height, both XBoard methods, HTTPS/DNS, webhook registration, disk/RAM/time sync, backups, restart/reboot recovery and logs. The existing production healthcheck guide describes per-service probes; no single `healthy` result establishes sync or payment readiness.

## Backup and rollback

Before plugin installation, make consistent encrypted backups of both XBoard and BTCPay databases, BTCPay datadir/configuration, sanitized compose/lock/plugin artifacts, and wallet through the approved version-specific wallet backup procedure. Verify file existence, hashes and restore in an isolated environment. Keep wallet ciphertext in two access-controlled offline/immutable locations, with decryption recovery under dual control. Never place seed/private key output in a terminal transcript.

Rollback order:

1. Stop new `BTCPayBTCX` checkouts at the XBoard channel/provider; leave original `BTCPay` enabled. Disable `btcpay_btcx` in PluginManager; do not remove its tables while invoices/webhook reconciliation may be needed.
2. Restore the prior standalone XBoard plugin source/config and prior BTCPay `.btcpay` artifact from the release record. The original BTCPay provider and its configuration remain untouched.
3. Restore the prior reviewed Compose and digest lock, then `docker compose ... up -d`; never run `down -v`, prune or delete node/wallet/database volumes for rollback.
4. Restore database backups only for a coordinated full recovery to a known point. A blind database rollback can discard valid original BTCPay invoices/orders and XBoard orders; reconcile post-backup activity first.
5. Preserve Bitcoin-PoCX wallet and chain data, electrs index, BTCPay datadir, both databases and all in-flight invoice/order IDs. Reconcile payment state before re-enabling callbacks or checkout.

## Mainnet activation and first controlled payment (not authorized in this audit)

No activation command is run here. `BTCX:Wallet:AllowMainnet` / `BTCX__Wallet__AllowMainnet` stays `false`. After every blocker is closed, a named release owner may open a separate change with two-person approval to set the explicit production value to `true`, verify runtime effective config and production environment guard, and then enable the store/provider. Do not enable this setting in development/staging.

The first future controlled payment requires its own finance/security authorization: use a dedicated production invoice and a pre-agreed minimal amount, verify CNY/rate/immutable BTCX snapshot and destination independently, send only from the approved BTCX wallet, wait for the full configured LowSpeed confirmation threshold (six), verify BTCPay payment/invoice and XBoard's signed `InvoiceSettled`/idempotent paid order, then reconcile node transaction and accounting records. This is a future procedure only: no amount, address, invoice, or transaction is created by this audit.

**Current stop point: NOT READY FOR MANUAL MAINNET ACTIVATION.** The audit must be repeated against the actual production host and completed release artifacts after the blockers above are resolved.
