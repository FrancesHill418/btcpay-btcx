# Production deployment status and future gate

**Production deployment is currently unsupported. This document is a control gate, not an executable mainnet runbook. Do not deploy the plugin for customer payments, connect it to BTCX mainnet, or use mainnet funds.**

The development-complete release baseline is BTCPay Server `v2.4.4` at commit `2d5a0d8077bb33af080e949031da33d84b80638d`, Bitcoin-PoCX commit `005bf0098e217b76a2627bfae458dff4f5718dd5` (bundled Bitcoin source v30.2.1, commit `b88b852644f629cd5f25b3424d11b462462c24b3`), bindex-btcx commit `eda7c70660baa06affef464c7ea1e131c39304f1`, and electrs-btcx `v0.11.1-btcx.1` at commit `2f78c63e20215e20944767f0901209c4d740fe5b`. Those node/indexer revisions have only been validated in isolated development regtest with the repository's PoCX `/rest/blockpart` compatibility backport. The backport is not an approved production component.

The plugin's wallet configuration rejects `Network=main` in this development build. Phoenix real-device E2E is pending. The default price source is an administrator-entered manual BTCX/CNY rate, not a reviewed market feed. RPC access currently grants the plugin node-wallet authority. These are release blockers, along with supported upstream node/indexer compatibility, production key custody and recovery, externally reviewed security operations, TLS/secret rotation, monitoring, and a reorg/merchant-compensation policy.

## Required approval gates before a production runbook can be written

1. A reviewed release of the plugin must intentionally support mainnet and pass a fresh code review; the current development build must not be bypassed through configuration edits.
2. Pin and validate supported production releases of BTCPay, Bitcoin-PoCX, bindex-btcx, and electrs-btcx. Resolve `/rest/blockpart` compatibility upstream or through an independently approved maintained release; do not carry the development patch forward without review.
3. Complete Phoenix real-device E2E on a safe non-mainnet network and define any remaining wallet compatibility constraints.
4. Approve a receiving-key architecture that avoids granting an internet-facing application spend-capable wallet access where practical. Document signing boundary, least privilege, encrypted/offline backup, restoration drills, rotation, and incident response.
5. Approve BTCX/CNY price sourcing, rate freshness/authorization policy, invoice quote behavior, and accounting controls. Manual staging rate entry is not a production pricing policy.
6. Approve RPC/indexer network isolation, firewall rules, TLS termination, secret injection/rotation, webhook authentication/replay handling, monitoring, alerting, retention, and incident response.
7. Define confirmation thresholds, reorg handling after fulfillment, customer support and compensation responsibilities.
8. Run and sign the production security review, threat model, disaster-recovery drill, and exact release checklist on a production-equivalent non-mainnet environment.

## Release artifact boundary

The eventual plugin build/install process is documented for isolated regtest in [staging-deployment.md](staging-deployment.md). Do not treat its development compatibility patch, node credentials, wallet, volumes, test XBoard configuration, or manual rate as production artifacts. No production deployment commands are provided until all gates above are resolved and separately approved.
