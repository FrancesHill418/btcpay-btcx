# Project state

**Synchronized:** 2026-09-28  
**Current implementation:** TASK 03.6 source-based URI/QR integration follows commit `0e9a54e`; use `git log -1` for the current commit.

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
              → (future) XBoard webhook fulfillment
```

XBoard's selected provider is its existing BTCPay Greenfield provider at the pinned commit below. BTCPay owns invoice lifecycle and checkout. The plugin registers `BTCX-OnChain`, a contextual manual rate provider, settings UI, immutable quote snapshot, BTCX network/address/amount primitives, private authenticated PoCX RPC, dedicated-wallet receiving address allocation, Electrs discovery, payment persistence, confirmation/reorg state mapping and Phoenix-compatible URI/QR. Live regtest and XBoard fulfillment have not yet been validated; do not accept customer payments.

## Locked versions and source revisions

| Component | Locked revision / version | Notes |
|---|---|---|
| BTCPay Server | `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` | Verified exact submodule tag and HEAD; target framework `net10.0`. |
| .NET SDK | `10.0.401`, `latestPatch` roll-forward | Repository `global.json`; runtime smoke host reported .NET runtime `10.0.12`. |
| XBoard | commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` | Greenfield provider source revision. |
| `bitcoin-pocx` | `005bf0098e217b76a2627bfae458dff4f5718dd5` | Node/consensus reference; no node integrated. |
| `btcx` | `v0.1.1`, commit `6907bacb132324e460cbe55d3765b4350bb56e61` | BTCX Rust wallet stack reference; not linked into plugin. |
| `bindex-btcx` | commit `eda7c70660baa06affef464c7ea1e131c39304f1` | Indexer reference; not integrated. |
| `electrs-btcx` | `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` | Electrum indexer reference; not integrated. |
| `esplora-pocx` | commit `2b7e1c8a5d2dde2d688974e5bdaf604d223283c8` | REST indexer reference; not integrated. |
| Phoenix PoCX | `v2.4.0`, commit `bc4713306c9c2cd3cbf989a3e355e0705b485218` | Source audit only; no Phoenix wallet round trip. |

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
- TASK 03.2 — PoCX RPC client; mock RPC tests and network-only queries. Commit `79e4d5f`.
- TASK 03.3 — dedicated node-wallet receive address allocation; mock JSON-RPC covered, no live wallet connected.
- TASK 03.0 — rate timestamp legacy serializer compatibility; unit covered, live Greenfield retrieval retest pending. Commit `5ea990c`.
- TASK 03.4 — Electrs/bindex script-history discovery and idempotent BTCPay payment sink. Commit `6f706c4`; mock covered only.
- TASK 03.5 — canonical confirmation state mapping and reorg/dropped-output reconciliation. In progress; policy follows pinned BTCPay v2.4.4 SpeedPolicy.
- TASK 03.6 — Phoenix-compatible `btcx:` URI and standard checkout QR. Implemented and unit/source verified; live Phoenix/regtest scan pending.

TASK 02.1 reports 29/29 ordinary tests and 30/30 runtime smoke test cases passed, with 0 build warnings and 0 errors. This does not mean BTCX can receive or settle a payment.

## Manual BTCX/CNY rate

First release does not use an exchange or Observatory market quote. The administrator configures `enabled`, `btcxCnyRate`, `updatedAt`, and source `manual`; the rate definition is `1 BTCX = X CNY`. The plugin's `IContextualRateProvider` reads current settings for each rate request. A disabled, absent, zero, negative, malformed, or invalid rate yields no BTCX quote and must block BTCX invoice creation. The CNY invoice amount is converted by BTCPay into BTCX at invoice creation. The prompt snapshot stores fiat amount/currency, BTCX amount/currency, rate, source, and timestamp; editing the setting affects new invoices and does not reprice old snapshots.

## Phoenix PoCX compatibility

Source audit is pinned to PoC-Consortium/phoenix-pocx `v2.4.0` above. Phoenix accepts `btcx:`, `pocx:`, legacy `bitcoin:`, and bare PoCX addresses as input; its canonical output scheme is `btcx:`. Its amounts are whole BTCX with 8 decimal places (`1 BTCX = 100,000,000` atomic units). BTCPay should emit invariant fixed-point amount text (e.g. `0.00000001`, not exponent notation) and a QR of the canonical `btcx:` URI. Network HRPs/prefixes are PoCX-specific and the send UI checks against its active network.

This establishes source-level URI/address/amount compatibility only. A mock-generated BTCX address is checked by the plugin's BTCX parser, but no Phoenix wallet runtime transfer or QR scan/send round trip has been executed. Full results and test design: [phoenix-pocx-compatibility.md](phoenix-pocx-compatibility.md).

## Current unimplemented functionality

- Verify real isolated regtest node/wallet startup, backup/restore and end-to-end allocation; current address provider is covered by mock RPC only.
- Verify address, fixed-decimal payment URI, and QR on a live isolated BTCPay checkout and Phoenix regtest wallet.
- Verify an isolated regtest Electrs/node/wallet payment flow and BTCPay PostgreSQL idempotency; current listener is mock tested only.
- Verify canonical confirmation thresholds, mempool eviction and reorg reversal in live regtest/BTCPay persistence.
- Verify an actual Phoenix PoCX receive/send round trip against generated plugin instructions.
- Complete XBoard-to-BTCPay webhook delivery and negative security-path integration tests.
- Harden XBoard provider behavior for event type, invoice state/identity, amount/currency/payment method/expiry/confirmation policy, and replay/idempotency before production fulfillment. The XBoard production source has not been modified.

## Current blockers

1. **Not payable:** Payment URI/QR and confirmation/reorg state mapping are implemented, but no live regtest payment has exercised BTCPay persistence. Do not use the current plugin for customer payments.
2. **Greenfield runtime retest pending:** a BTCX-scoped converter now reads the previously observed integer `rateTimestamp` and emits canonical UTC ISO strings. The isolated Greenfield `includePaymentMethods` runtime smoke still needs to be rerun; BTCPay core remains unchanged.
3. **No live regtest integration:** RPC/wallet calls have only mock tests; no BTCX node, wallet, or indexer has been started or connected. Mainnet must remain out of scope.
4. **XBoard webhook trust checks incomplete:** source audit found the current provider does not enforce the full event/invoice/amount/currency/method/expiry/idempotency policy required for fulfillment; end-to-end callback tests remain outstanding.
5. **Phoenix evidence is source-only:** generated fixed-point URI vectors match the pinned parser contract, but QR scanning and a real Phoenix regtest wallet transaction remain untested.

## Next stage

**Current TASK = TASK 03.6 — Phoenix URI/QR; source-based implementation is complete. Next is completing the end-to-end BTCX payment method.** The repository task plan records source and regtest requirements; preserve those constraints.

## Core/source boundary

No BTCPay Server core files or XBoard production files are changed. Do not deploy production, connect BTCX mainnet, or enable customer payments while the blockers above remain.
