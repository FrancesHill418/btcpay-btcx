# Codex handoff — TASK 03 preparation

## Current state

- Repository HEAD at handoff: `e93ee67513f254326b9eea69b606855f15ef9b3b` (`docs: synchronize project state and Phoenix compatibility`).
- Working tree was clean when this audit started. TASK 02.3 is documentation-only and adds this handoff plus `task-03-plan.md`, `btcx-implementation-map.md`, and `btcx-payment-sequence.md`.
- Completed: TASK 01, 01.5, 01.6, 01.6.1, 01.6.2, 01.6.3, 01.7, 02, 02.1, 02.2.
- Next stage: TASK 03, not started. First implementation should be TASK 03.0 prompt serialization compatibility fix/test, then network/address primitives; see `docs/task-03-plan.md`.
- Current plugin is still a skeleton and cannot actually receive BTCX payments: no unique receive address allocation, URI/QR payment prompt, node/indexer integration, transaction detection, confirmation tracking or real settlement.

## Fixed versions and architecture

- Target BTCPay: `v2.4.4`, submodule commit `2d5a0d8077bb33af080e949031da33d84b80638d`.
- Project runtime observed in prior baseline: .NET SDK `10.0.401`, plugin target `net10.0`.
- XBoard commit: `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`.
- bitcoin-pocx root: `005bf0098e217b76a2627bfae458dff4f5718dd5`; Bitcoin source submodule: `b88b852644f629cd5f25b3424d11b462462c24b3`.
- BTCX Rust wallet: `6907bacb132324e460cbe55d3765b4350bb56e61`.
- electrs-btcx: `2f78c63e20215e20944767f0901209c4d740fe5b`; bindex-btcx: `eda7c70660baa06affef464c7ea1e131c39304f1`.
- esplora-pocx frontend/docs audit: `2b7e1c8a5d2dde2d688974e5bdaf604d223283c8`; actual backend `esplora-electrs-pocx` is not pinned/audited.
- Phoenix PoCX: `v2.4.0`, `bc4713306c9c2cd3cbf989a3e355e0705b485218`.
- Architecture: XBoard CNY order → BTCPay Greenfield API → BTCPay 2.4.4 → BTCX plugin/payment method. Manual rate `1 BTCX = X CNY`; each invoice must lock fiat/crypto amounts, currencies, rate, source and timestamp. Later rate changes affect new invoices only.

## Exact first files to inspect

1. `src/BTCPayServer.Plugins.BTCX/Payments/BtcxPaymentMethodHandler.cs` and `Payments/BtcxInvoiceSnapshot.cs` — resolve runtime prompt serialization failure before adding fields.
2. `tests/BTCPayServer.Plugins.BTCX.Tests/RuntimeSmokeTests.cs` — use existing gated development host/Greenfield path; add persisted prompt retrieval with payment methods included.
3. `submodules/btcpayserver/BTCPayServer/Payments/IPaymentMethodHandler.cs`, `Payments/Bitcoin/BitcoinLikePaymentHandler.cs`, `Services/Invoices/InvoiceRepository.cs` — exact prompt/tracked destination persistence contract.
4. `submodules/btcpayserver/BTCPayServer/Payments/Bitcoin/NBXplorerListener.cs`, `Services/Invoices/PaymentService.cs`, `HostedServices/InvoiceWatcher.cs` — payment and invoice lifecycle; do not assume generic listener API.
5. Pinned PoCX source `src/primitives/block.h`, `src/primitives/block.cpp`, `src/kernel/chainparams.cpp`; pinned Rust BTCX `params-btcx/src/params.rs`; Phoenix `payment-uri.ts` and address validation.

## First implementation step

Implement only the prompt-details timestamp serialization compatibility fix and a regression test: create the existing manual-rate BTCX invoice, serialize/persist/retrieve it through the BTCPay v2.4.4 host, parse `BtcxInvoiceSnapshot`, and verify `rateTimestamp`, fiat/crypto amount, rate/source survive unchanged. Include Greenfield retrieval with payment methods, because runtime audit observed `JsonReaderException: Unexpected token Integer` at `rateTimestamp`. Then stop and review before address or chain work.

## Known risks and blockers

1. **Runtime blocker:** persisted prompt `rateTimestamp` integer causes parser failure in a retrieval path. Confirmed during runtime smoke; fix before checkout/payment work.
2. **No receiving implementation:** current BTCX payment link returns null; checkout only exposes skeleton data; no address, wallet, transaction listener or settlement exists.
3. **PoCX parser mismatch:** block header is 286 bytes with PoCX-specific fields and hash behavior; ordinary Bitcoin/NBitcoin/NBXplorer assumptions are unsafe.
4. **Index service pinning:** electrs/bindex commits have been source-audited but not end-to-end regtest verified. Esplora backend is not pinned.
5. **Wallet security decision:** node wallet RPC is simplest for regtest but holds hot keys; production requires explicit watch-only/address service, backup/recovery and isolation decision. Receiving only; no withdrawal.
6. **Confirmation/reorg policy:** BTCX settlement confirmation threshold and late-payment behavior need an explicit policy; BTCPay has no generic `Confirmed` payment status.
7. **XBoard webhook:** current provider does not validate all required event/invoice/payment/order invariants; no production fulfillment proof. Separate hardening remains required.
8. **Phoenix compatibility:** pinned Phoenix accepts `btcx:` and amount query, but plugin URI/QR has not been generated or round-trip tested yet.

## Non-negotiable boundaries

- Do not start full TASK 03 without an explicit task request.
- Do not edit BTCPay core/submodule, XBoard production code or BTCX upstream source.
- Do not connect BTCX mainnet or production services as part of the first implementation stages.
- Keep private keys, seeds, RPC auth and webhook secrets out of source, logs and docs.
- Store BTCX money in integer atomic units at detection/settlement boundaries; preserve invoice rate snapshot immutably.
- Notifications are hints; recover by reconciliation after restarts and reorgs. Indexer observations alone never authorize settlement.
