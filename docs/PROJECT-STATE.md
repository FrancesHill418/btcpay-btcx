# Project state

**Synchronized:** 2026-09-28  
**Current implementation:** MILESTONE 03 passed a gated BTCPay/PostgreSQL + BTCX regtest acceptance using real electrs-btcx against a development-only PoCX REST compatibility backport; wallet backup/restore passed. XBoard live order→Greenfield→BTCX regtest payment→confirmation→signed webhook→paid-order E2E passed in an isolated environment. Phoenix parser is **PROTOCOL COMPATIBLE**, but **REAL DEVICE E2E VERIFIED: NO** because no device/emulator runtime is available. Development validation remains incomplete on the Phoenix device gate. See [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).

**FINAL RELEASE PACKAGING staging E2E (2026-09-28): PASS.** Store `2VfNxGeK1aaQEVitL4TvY5HcVbaK1wK9FPxzxmUGeebm` is configured with the BTCX-CHAIN payment method through Greenfield and uses the plugin's existing dedicated PoCX wallet RPC (`btcx-receive`, regtest). For CNY 12.34 at 0.20 CNY/BTCX, BTCPay created a 61.7 BTCX invoice, served the checkout URI/QR, and received a real 61.7 BTCX regtest transaction. The plugin listener observed it; six confirmations settled the invoice; BTCPay delivered a signed `InvoiceSettled` event (HTTP 200); and XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` marked the matching order paid. This PASS is staging/regtest only, not mainnet or production approval. See [staging deployment](staging-deployment.md) and [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).

## Final validation status

| Validation | Status | Evidence / scope |
|---|---|---|
| Prior isolated XBoard Live E2E | **PASS (historical)** | Earlier isolated acceptance: CNY 12.34 → 61.7 BTCX regtest → actual settlement webhook → paid order. This does not establish success in the final Compose staging deployment. |
| Final Compose BTCX payment → XBoard E2E | **PASS** | XBoard order `2026092823095172496666658` → invoice `Ck9zoSuwTrwCUUTQFvurjz`; signed `InvoiceSettled` delivery HTTP 200; matching order status `3` (paid). |
| BTCX regtest in final Compose | **PASS** | Real 61.7 BTCX transaction `84e204e54c15b94fe847d24d13cb1c58c2d8dcb980c0d491fe73ca68a1c8314e`; plugin listener recorded it and six confirmations settled the invoice. |
| electrs-btcx | **PASS** | Current regtest REST from node/electrs network, Electrum `server.version`, and index/node height 209 match passed. Historical address/history/UTXO/transaction/confirmation tests also passed with the documented PoCX REST compatibility backport. |
| Phoenix protocol compatibility | **PASS** | Pinned Phoenix PoCX parser accepted the BTCX URI fixture. |
| Phoenix real-device E2E | **PENDING** | No attached device, `adb`, Flutter runtime, Android SDK/emulator or Phoenix installable was available; QR scan/send/receive was not verified. |
| `dotnet build` | **PASS** | 0 warnings, 0 errors. |
| `dotnet test` | **PASS** | 106 passed, 0 failed, 1 gated runtime smoke skipped; prior isolated runtime smoke passed 107/107. |
| RELEASE PACKAGING Compose health | **PASS** | regtest node, electrs-btcx, PostgreSQL and BTCPay healthy; plugin loaded; node REST 200 from node/electrs namespace and service DNS; Electrum indexed height 209 matched node height 209. |
| RELEASE PACKAGING BTCX payment → XBoard E2E | **PASS** | Greenfield BTCX-CHAIN invoice, real regtest payment, six confirmations, signed webhook, and matching paid XBoard order verified in the final stack. |

## Project goal

Provide BTCX as an externally maintained BTCPay Server payment-method plugin so XBoard can create CNY orders through BTCPay Greenfield, present BTCX payment instructions, observe valid BTCX payments, and only then notify XBoard for fulfillment. Do not modify BTCPay Server core or XBoard production code as part of plugin implementation.

## Current architecture

```text
XBoard CNY order
  → BTCPay Greenfield API v1
    → BTCPay Server v2.4.4 CNY invoice
      → BTCX plugin payment method and manual BTCX/CNY quote
        → PoCX node RPC and invoice-labeled receive address allocation
          → electrs-btcx script-history discovery and BTCPay payment persistence
            → BTCPay SpeedPolicy confirmations with canonical reorg recovery
            → Phoenix `btcx:` payment URI and QR
              → XBoard Greenfield provider + invoice-bound HMAC webhook validation
```

XBoard's selected provider is its existing BTCPay Greenfield provider at the pinned commit below; its provider-only implementation patch is preserved under `integrations/xboard`. BTCPay owns invoice lifecycle and checkout. The plugin registers `BTCX-CHAIN`, a contextual manual rate provider, settings UI, immutable quote snapshot, BTCX network/address/amount primitives, private authenticated PoCX RPC, dedicated-wallet receive address allocation, Electrum history discovery, payment persistence, confirmation/reorg state mapping and Phoenix-compatible URI/QR. The gated runtime smoke and isolated XBoard live E2E exercised the real regtest node, wallet RPC, electrs discovery, BTCPay payment state machine, Greenfield invoice, signed settlement callback and XBoard order completion. The pinned PoCX v30.2.1 REST backport used for development verification is preserved under `integrations/electrs-btcx`; the unpatched upstream node remains incompatible with pinned bindex. Phoenix real-device validation remains unavailable; do not accept customer payments.

## Locked versions and source revisions

| Component | Locked revision / version | Notes |
|---|---|---|
| BTCPay Server | `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` | Verified exact submodule tag and HEAD; target framework `net10.0`. |
| .NET SDK | `10.0.401`, roll-forward disabled | Repository `global.json`; runtime smoke host reported .NET runtime `10.0.12`. |
| XBoard | commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` | Greenfield provider source revision. |
| `bitcoin-pocx` | `005bf0098e217b76a2627bfae458dff4f5718dd5` | Isolated regtest node/wallet used for runtime smoke; node is not integrated into BTCPay. |
| `btcx` | `v0.1.1`, commit `6907bacb132324e460cbe55d3765b4350bb56e61` | BTCX Rust wallet stack reference; not linked into plugin. |
| `bindex-btcx` | commit `eda7c70660baa06affef464c7ea1e131c39304f1` | External indexer component used in isolated regtest acceptance; not vendored into the plugin. |
| `electrs-btcx` | `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` | External indexer service used in isolated regtest acceptance; not vendored into the plugin. |
| `esplora-pocx` | commit `2b7e1c8a5d2dde2d688974e5bdaf604d223283c8` | REST indexer reference; not integrated. |
| Phoenix PoCX | `v2.4.0`, commit `bc4713306c9c2cd3cbf989a3e355e0705b485218` | Pinned parser fixture executed; no device/QR/wallet round trip. |

More detailed baseline/environment records: [baseline.md](baseline.md). The historical v2.4.1 proposal in the original baseline has been superseded by the locked and runtime-tested v2.4.4 revision.

## Completed tasks

- TASK 01 — environment/repository baseline work.
- TASK 01.5 — dependency and architecture audit.
- TASK 01.6 — BTCPay 2.4.4 Greenfield/XBoard compatibility audit and architecture decision.
- TASK 01.6.1 — rate-source and Greenfield smoke-test research/design.
- TASK 01.6.2 — BTCX Observatory valuation-source audit.
- TASK 01.6.3 — manual BTCX/CNY rate design.
- TASK 01.7 — Debian development environment preparation and verification.
- TASK 02 — BTCX BTCPay plugin skeleton, settings, contextual rate provider, invoice quote snapshot and unit tests.
- TASK 02.1 — isolated BTCPay runtime plugin load/Greenfield checkout smoke test.
- TASK 02.2 — Phoenix PoCX source compatibility audit and test design.
- TASK 03.1 — BTCX network, address/script and atomic amount primitives; 50/50 tests at completion.
- TASK 03.2 — PoCX RPC client; mock tests plus gated loopback regtest host validation. Commit `79e4d5f`.
- TASK 03.3 — dedicated node-wallet receive address allocation; gated smoke allocated a real regtest receive address through BTCX wallet RPC.
- TASK 03.0 — rate timestamp legacy serializer compatibility; unit and Greenfield `includePaymentMethods` runtime retrieval covered. Commit `5ea990c`.
- TASK 03.4 — Electrs/bindex history discovery and idempotent payment sink implemented. Real electrs-btcx indexed and served address history, UTXO, raw transaction, mempool and confirmation against an isolated development build with the preserved `/rest/blockpart` backport. The unpatched v30.2.1 node lacks this required route.
- TASK 03.5 — confirmation/reorg reconciliation. Gated smoke verified 6-confirmation settlement, Processing after block invalidation, and resettlement after reconsideration.
- TASK 03.6 — Phoenix-compatible `btcx:` URI/checkout QR. Plugin URI vectors and Phoenix v2.4.0 parser fixture pass; no device/QR scan E2E.
- TASK 03.7 — complete BTCX payment flow. Real regtest wallet RPC, electrs discovery, BTCPay invoice/payment persistence, confirmation and reorg transitions passed.
- MILESTONE 04 — XBoard provider patch binds CNY invoices to BTCX-only order terms and validates signed settlement callbacks. Pinned tests pass 6 tests / 25 assertions. Isolated live XBoard E2E passed exact BTCX regtest payment, confirmation, successful HMAC-protected `InvoiceSettled` webhook delivery (HTTP 200), matching invoice/order IDs and XBoard paid state. Live duplicate delivery was not separately triggered; provider duplicate tests pass.
- MILESTONE 05 — source security review and runtime checks documented in [security-final.md](security-final.md); development wallet recovery passed, while production receiving-key and indexer boundaries remain gates.

`dotnet restore` and `dotnet build` pass (0 warnings/errors). The current `dotnet test` run passed 106 tests, failed 0 and skipped one gated runtime smoke; the gated suite using isolated loopback PostgreSQL and real electrs previously passed 107/107. Exact 37.5 BTCX regtest payment settled at six confirmations and reversed/resettled through invalidate/reconsider; wallet backup/restore recovered all 9 labels and 5 wallet transactions. The live XBoard E2E added a 61.7 BTCX regtest transaction (`d1902feecaff154d8734f51c1e6238e867e1507971d860975d62061f40c331ab`) against CNY 12.34 at snapshotted rate 0.20; BTCPay settled the invoice and delivered the actual `InvoiceSettled` webhook with HTTP 200, and XBoard order status became paid. No Phoenix device E2E was possible: `adb` and Flutter are absent, and `/dev/bus/usb` is absent. See [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).

## Manual BTCX/CNY rate

First release does not use an exchange or Observatory market quote. The administrator configures `enabled`, `btcxCnyRate`, `updatedAt`, and source `manual`; the rate definition is `1 BTCX = X CNY`. The plugin's `IContextualRateProvider` reads current settings for each rate request. A disabled, absent, zero, negative, malformed, or invalid rate yields no BTCX quote and must block BTCX invoice creation. The CNY invoice amount is converted by BTCPay into BTCX at invoice creation. The prompt snapshot stores fiat amount/currency, BTCX amount/currency, rate, source, and timestamp; editing the setting affects new invoices and does not reprice old snapshots.

## Phoenix PoCX compatibility

Source audit is pinned to PoC-Consortium/phoenix-pocx `v2.4.0` above. Phoenix accepts `btcx:`, `pocx:`, legacy `bitcoin:`, and bare PoCX addresses as input; its canonical output scheme is `btcx:`. Its amounts are whole BTCX with 8 decimal places (`1 BTCX = 100,000,000` atomic units). BTCPay should emit invariant fixed-point amount text (e.g. `0.00000001`, not exponent notation) and a QR of the canonical `btcx:` URI. Network HRPs/prefixes are PoCX-specific and the send UI checks against its active network.

The pinned Phoenix `parsePaymentUri` was executed from source against the BTCPay BTCX regtest URI fixture; 37.5 BTCX, one atomic unit and rejection of a Bitcoin-address URI passed. **PROTOCOL COMPATIBLE** is confirmed. **REAL DEVICE E2E VERIFIED** is not: no Phoenix device/runtime, QR camera scan, or wallet send/receive round trip ran. Full results and test design: [phoenix-pocx-compatibility.md](phoenix-pocx-compatibility.md).

## Remaining development and release gates

- Complete Phoenix PoCX real-device E2E: scan the BTCPay checkout QR, verify the displayed address/amount, send BTCX regtest, and confirm BTCPay settlement. No device, Android SDK/emulator, or Phoenix installable was available in this environment.
- Keep the development-only PoCX REST compatibility backport operationally pinned or upstreamed before production; the unmodified pinned node source lacks the blockpart endpoint required by pinned bindex/electrs.
- Exercise wallet recovery across BTCPay/node process restarts and define encrypted/offline production backup handling.
- Decide the production receiving-key boundary, an approved BTCX/CNY price source, and merchant compensation for deep reorgs after fulfillment.

## Current blockers

1. **Phoenix device validation:** pinned parser fixture is protocol compatible, but no real device, QR scan, or wallet send/receive round trip ran. Host inspection found no `adb`, Flutter, Android SDK/emulator, Phoenix installable, or attached USB device.
2. **Upstream node/indexer compatibility:** the isolated electrs regtest passed with the documented PoCX REST backport; the unmodified pinned node still lacks `/rest/blockpart/<genesis>.bin?offset=0&size=491`. No second production listener or RPC fallback was added.
3. **Production controls:** node wallet RPC has spend-capable authority; production requires a reviewed receiving-key design, encrypted/offline recovery, approved BTCX/CNY pricing, TLS/secret rotation and reorg policy. Do not enable customer payments based only on these development tests.

## Next stage

**MILESTONE 03 BTCX regtest payment, electrs, confirmation and reorg acceptance passed with the documented development node compatibility backport. MILESTONE 04 live exact-payment XBoard E2E and callback/order completion passed; provider tests cover duplicate delivery, underpayment, overpayment and expired invoices. Final development validation remains incomplete until Phoenix real-device E2E is run.**

## Core/source boundary

No BTCPay Server core files or XBoard production files are changed. Do not deploy production, connect BTCX mainnet, or enable customer payments while the blockers above remain.

## Release preparation

The `development-complete` tag points to baseline commit `57995b3ecc154069c94d06c967cbac2c54ab3324`. The repeatable isolated staging procedure, production deployment gates, backup/recovery guidance, and release checklist are in [staging-deployment.md](staging-deployment.md), [production-deployment.md](production-deployment.md), [backup-and-recovery.md](backup-and-recovery.md), and [release-checklist.md](release-checklist.md). Staging uses regtest only and its full payment/XBoard E2E passed as recorded above. Production remains unsupported: mainnet is behind a default-off explicit configuration gate, Phoenix real-device E2E is pending, the node/indexer compatibility patch is development-only, and production key custody/rate/recovery controls are unresolved. Release archives must exclude private keys, wallet seeds, API keys, webhook secrets, `.env` files, databases, Docker volumes, and BTCX blockchain data.

## Production release candidate preparation (2026-09-28)

The prior release-preparation snapshot and its `v0.1.0-rc1` tag remain unchanged. Follow-up hardening is in progress on the current branch: mainnet is now guarded by default-off `BTCX:Wallet:AllowMainnet`; base-image and local staging image references are digest-pinned in Compose/Dockerfiles; the XBoard provider patch reads credentials from mounted secret files; and [image-lock.md](image-lock.md) records inspected digests and source revisions. This work is **not** `v0.1.0-rc2`. Production remains **NO-GO / NOT AUTHORIZED** because apt package inputs are floating, local custom images are unpublished and staging node image drift is unresolved, upstream PoCX/indexer production artifacts are not approved, Phoenix real-device E2E is unavailable here, and wallet custody, staging rerun, production DR, Cloudflare/TLS and security reviews remain open. Do not deploy, connect BTCX mainnet, or send mainnet funds.

Current staging service names were resolved from `docker compose config`/`ps`: `bitcoin-pocx`, `electrs-btcx`, `postgres`, `wallet-init`, and `btcpay`. Electrs deliberately uses the node service network namespace because pinned bindex-btcx requests Bitcoin REST on localhost. Do not change its RPC/REST target to service DNS while this topology remains in place. The final regtest staging acceptance is **PASS** as recorded above.

## Final production packaging checkpoint (2026-09-29)

The .NET 10 BTCX plugin release package was produced locally with BTCPay PluginPacker and is installable in a BTCPay test instance; the `.btcpay` artifact is intentionally not committed. The BTCX official-generator overlay, generated Compose snapshot, non-secret environment template, Bitcoin-PoCX/electrs/BTCPay build sources, backup, healthcheck, deployment, and smoke-test guides are present. Official generator output passed `docker compose config`; root Compose passed config validation. `dotnet build` and `dotnet test` passed (107 tests passed, one gated runtime host smoke skipped).

The electrs candidate image built locally from the pinned electrs/bindex commits, and the thin BTCPay v2.4.4 wrapper image built locally. The Bitcoin-PoCX image build was cancelled while downloading its pinned toolchain, before source compilation. Candidate images have no published registry digests, and no production-like E2E ran against these final candidate artifacts. Phoenix real-device E2E, upstream patch review, production wallet restore, secret-manager/XBoard final-stack check and other gates remain open. This is **PRODUCTION BLOCKED / release candidate packaging only**; do not deploy, connect mainnet, create production wallet, contact production XBoard, or send funds.

## Production hardening checkpoint (2026-09-29)

Hardening changes are committed separately from release tags. Mainnet now has an explicit `BTCX:Wallet:AllowMainnet` gate with default false; a simulated RPC test confirms default refusal and opt-in address allocation. Root staging Compose and base Dockerfiles use tag-plus-digest references; SDK roll-forward is disabled. [`docs/image-lock.md`](image-lock.md) records exact refs, patch hashes and drift. The XBoard provider patch reads Greenfield/HMAC credentials from read-only mounted secret files; on exact XBoard commit its patch applies and PHPUnit passes 7 tests / 28 assertions. BTCX focused tests pass 6/6.

This is not RC2. Remaining blockers: apt repositories/packages and NuGet transitive restore are not locked; custom image source provenance is incomplete and images are local-only; the running node image differs from the current local locked tag; compatible supported PoCX/indexer artifacts remain unapproved; Phoenix device E2E cannot run here (no `adb`, Flutter or attached device); the secret-file patch has not passed full staging E2E; production wallet custody, production database/wallet recovery, TLS/Cloudflare, operational policies and independent security approval remain outstanding. No production, BTCX mainnet or real funds were contacted.
