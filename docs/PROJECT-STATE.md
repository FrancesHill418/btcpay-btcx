# Project state

**Synchronized:** 2026-09-28  
**Current implementation:** MILESTONE 03 passed a gated BTCPay/PostgreSQL + BTCX regtest acceptance using real electrs-btcx against a development-only PoCX REST compatibility backport; wallet backup/restore passed. XBoard provider tests pass, but live XBoard app-to-app E2E remains unverified. See [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).

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

XBoard's selected provider is its existing BTCPay Greenfield provider at the pinned commit below; its provider-only implementation patch is preserved under `integrations/xboard`. BTCPay owns invoice lifecycle and checkout. The plugin registers `BTCX-OnChain`, a contextual manual rate provider, settings UI, immutable quote snapshot, BTCX network/address/amount primitives, private authenticated PoCX RPC, dedicated-wallet receiving address allocation, Electrum history discovery, payment persistence, confirmation/reorg state mapping and Phoenix-compatible URI/QR. The gated runtime smoke exercised a real regtest node, wallet RPC, real electrs discovery and BTCPay payment state machine. The pinned PoCX v30.2.1 REST backport used for this development verification is preserved under `integrations/electrs-btcx`; the unpatched upstream node remains incompatible with pinned bindex. XBoard provider tests pass, but live cross-application E2E remains unverified; do not accept customer payments.

## Locked versions and source revisions

| Component | Locked revision / version | Notes |
|---|---|---|
| BTCPay Server | `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` | Verified exact submodule tag and HEAD; target framework `net10.0`. |
| .NET SDK | `10.0.401`, `latestPatch` roll-forward | Repository `global.json`; runtime smoke host reported .NET runtime `10.0.12`. |
| XBoard | commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` | Greenfield provider source revision. |
| `bitcoin-pocx` | `005bf0098e217b76a2627bfae458dff4f5718dd5` | Isolated regtest node/wallet used for runtime smoke; node is not integrated into BTCPay. |
| `btcx` | `v0.1.1`, commit `6907bacb132324e460cbe55d3765b4350bb56e61` | BTCX Rust wallet stack reference; not linked into plugin. |
| `bindex-btcx` | commit `eda7c70660baa06affef464c7ea1e131c39304f1` | Indexer reference; not integrated. |
| `electrs-btcx` | `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` | Electrum indexer reference; not integrated. |
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
- MILESTONE 04 — XBoard provider patch binds CNY invoices to BTCX-only order terms and validates signed settlement callbacks. Pinned tests pass 6 tests / 26 assertions; live XBoard/BTCPay E2E remains unverified.
- MILESTONE 05 — source security review and runtime checks documented in [security-final.md](security-final.md); development wallet recovery passed, while production receiving-key and indexer boundaries remain gates.

`dotnet restore` and `dotnet build` pass (0 warnings/errors). Final ordinary `dotnet test --no-build` passed 106 tests and skipped one gated runtime smoke; the gated suite using an isolated local node cookie, independent loopback PostgreSQL and real electrs at `127.0.0.1:50401` passed 107/107. It loaded BTCPay v2.4.4, created/retrieved BTCX CNY invoices, rendered checkout, allocated a real BTCX regtest wallet address, sent an exact 37.5 BTCX transaction, discovered/persisted it through electrs and the listener, settled at six confirmations, and reversed/resettled payment state through invalidate/reconsider. A separate 12.345 BTCX transaction validated electrs mempool/history/UTXO/raw transaction then confirmation indexing. Live XBoard/Phoenix device E2E remain unverified. Wallet backup/restore recovered all 9 labels and 5 wallet transactions. See [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).

## Manual BTCX/CNY rate

First release does not use an exchange or Observatory market quote. The administrator configures `enabled`, `btcxCnyRate`, `updatedAt`, and source `manual`; the rate definition is `1 BTCX = X CNY`. The plugin's `IContextualRateProvider` reads current settings for each rate request. A disabled, absent, zero, negative, malformed, or invalid rate yields no BTCX quote and must block BTCX invoice creation. The CNY invoice amount is converted by BTCPay into BTCX at invoice creation. The prompt snapshot stores fiat amount/currency, BTCX amount/currency, rate, source, and timestamp; editing the setting affects new invoices and does not reprice old snapshots.

## Phoenix PoCX compatibility

Source audit is pinned to PoC-Consortium/phoenix-pocx `v2.4.0` above. Phoenix accepts `btcx:`, `pocx:`, legacy `bitcoin:`, and bare PoCX addresses as input; its canonical output scheme is `btcx:`. Its amounts are whole BTCX with 8 decimal places (`1 BTCX = 100,000,000` atomic units). BTCPay should emit invariant fixed-point amount text (e.g. `0.00000001`, not exponent notation) and a QR of the canonical `btcx:` URI. Network HRPs/prefixes are PoCX-specific and the send UI checks against its active network.

The pinned Phoenix `parsePaymentUri` was executed from source against the BTCPay BTCX regtest URI fixture; 37.5 BTCX, one atomic unit and rejection of a Bitcoin-address URI passed. **PROTOCOL COMPATIBLE** is confirmed. **REAL DEVICE E2E VERIFIED** is not: no Phoenix device/runtime, QR camera scan, or wallet send/receive round trip ran. Full results and test design: [phoenix-pocx-compatibility.md](phoenix-pocx-compatibility.md).

## Remaining development and release gates

- Make pinned electrs-btcx compatible with this PoCX node REST API (or replace the single production discovery backend after separate review); verify indexed history, UTXO and restart recovery.
- Run XBoard + Greenfield + BTCPay callback/order completion in a live isolated deployment; current XBoard tests use Laravel HTTP fakes and SQLite.
- Run Phoenix PoCX on a device/runtime to scan the generated checkout QR and send a regtest transaction.
- Exercise wallet recovery across BTCPay/node process restarts and define encrypted/offline production backup handling.
- Decide the production receiving-key boundary, an approved BTCX/CNY price source, and merchant compensation for deep reorgs after fulfillment.

## Current blockers

1. **Indexer compatibility:** electrs-btcx 0.11.1/bindex-btcx `eda7c706` exits at startup with HTTP 404 for `/rest/blockpart/<genesis>.bin?offset=0&size=491`; see MILESTONE 06. No second production listener or RPC fallback was added.
2. **XBoard end-to-end acceptance:** provider behavior and negative security paths are isolated-test verified; live Greenfield webhook delivery and order completion remain unverified.
3. **Phoenix device validation:** pinned parser fixture passes, but no real device, QR scan, or wallet send/receive round trip ran.
4. **Production controls:** node wallet RPC has spend-capable authority; production requires a reviewed receiving-key design, encrypted/offline recovery, approved BTCX/CNY pricing, TLS/secret rotation and reorg policy. Do not enable customer payments based only on these development tests.

## Next stage

**MILESTONE 03 plugin acceptance passed with an Electrum fixture; pinned electrs compatibility is an explicit blocker. MILESTONE 04 provider implementation/tests pass, while live cross-application E2E remains open. Security review is recorded; proceed only to manual review and targeted follow-up, not deployment or mainnet.**

## Core/source boundary

No BTCPay Server core files or XBoard production files are changed. Do not deploy production, connect BTCX mainnet, or enable customer payments while the blockers above remain.
