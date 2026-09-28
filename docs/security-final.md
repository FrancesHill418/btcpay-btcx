# Security and reliability review

Review date: 2026-09-28. Scope: BTCX plugin at the current development revision and XBoard provider patch against `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`. Isolated regtest node/wallet, loopback PostgreSQL and development node-cookie authentication were used; no production service or credential was accessed. Source scans found no PEM/xprv key material or credential files. The only credential-like values are explicit test placeholders in the runtime smoke and XBoard unit fixtures.

## RPC and discovery

- BTCX node RPC and Electrum endpoints reject public literal IPs. At connection time, hostname resolution is filtered to private/loopback/link-local addresses; only approved resolved addresses are attempted. RPC has Basic authentication by secret configuration or node cookie, bounded timeout/retry, cancellation and bounded retry behavior. Electrum has no authentication in this integration and must remain on an internal network.
- RPC request/response bodies and authorization values are not logged. Listener logs contain invoice IDs and exception type only. Authentication errors use a generic message.
- The Electrum client and listener are disabled by default; the listener refuses mainnet. Wallet options also refuse mainnet. The development configuration therefore cannot allocate to a BTCX mainnet wallet.
- The node cookie/password is powerful node-wallet authentication, even though plugin calls use only receive-address methods. Keep the development cookie and wallet directory private. The wallet itself is spend-capable. Before any value-bearing deployment, replace this arrangement with a separately reviewed watch-only descriptor/xpub allocator or isolated address service. No withdrawal capability is implemented.

## Wallet and recovery

- The plugin stores receive address, script, network and amount in its invoice snapshot and uses a stable invoice-derived wallet label to recover an address after restart or an ambiguous allocation response. It does not store seed/private key material.
- The gated development run backed up `btcx-receive` with `backupwallet` and restored it as a separate wallet. All 9 labels/receive addresses and all 5 wallet transaction IDs matched. The temporary mode-0600 backup was removed after the test. This does not verify encrypted/offline storage or a service restart.
- Address ownership is tied to the configured dedicated wallet and label lookup. Regtest restore verified label/address recovery; production backup/recovery remains unverified.

## Payment, confirmations and invoice binding

- Listener output identity is `{network, txid, vout}` and BTCPay persistence is the idempotency boundary. Electrum only discovers; PoCX node RPC validates output scripts and canonical block membership. Incomplete index/node observations do not demote payment state.
- Reorg/dropped-output reconciliation can move a payment to `Unaccounted`; re-mined/mempool outputs are reconciled again. BTCPay v2.4.4 SpeedPolicy controls confirmation thresholds. A deep reorg after XBoard has already fulfilled an order requires merchant compensation; XBoard core does not provide a reversible fulfillment operation and was not modified.
- Runtime validation confirmed a v2.4.4 lifecycle limitation: after invoice aggregate state reaches `Settled`, `InvoiceWatcher` leaves that invoice status sticky while the BTCX payment itself can correctly revert from `Settled` to `Processing` after reorg. The XBoard provider checks the individual BTCX payment status as well as invoice state, so such a webhook cannot complete an unpaid order during this mismatch. Already completed XBoard orders still need manual compensation after a deep reorg.
- XBoard's provider patch binds one invoice to one exact order and checks signed event, invoice status, metadata, CNY amount, BTCX payment, checkout policy and delivery identity before returning its order mapping. Provider tests cover duplicate delivery; XBoard's existing order-paid path is the final order idempotency boundary. The API token and HMAC secret use configuration values, and provider error messages do not echo them. Live HTTP replay/idempotency remains unverified.
- Manual BTCX/CNY rate validation is positive, decimal-safe, administrator-controlled, and snapshotted on each invoice. Editing the rate affects only new invoices.

## Verified limits and remaining release gates

- Real electrs-btcx was attempted against the pinned regtest node. It exits at startup because `/rest/blockpart/<genesis>.bin?offset=0&size=491` returns HTTP 404; bindex-btcx requires the endpoint and emits `use https://github.com/bitcoin/bitcoin/pull/33657`. No fallback listener was introduced. See [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).
- The gated BTCPay payment lifecycle passed with real node/wallet RPC and an Electrum fixture; only actual indexer discovery remains unverified.
- Phoenix v2.4.0 parser fixture is **PROTOCOL COMPATIBLE**. There is no real device/QR/send/receive E2E evidence.
- XBoard provider tests pass with in-memory SQLite and HTTP fakes. There is no live Greenfield callback or XBoard order-completion evidence.
- The manual rate is administrator-entered. No trustworthy native BTCX/CNY market source has been approved.
- Before any manual mainnet review, replace/review the spend-capable node wallet authority, complete encrypted/offline backup and restore drills, fix the PoCX/electrs compatibility or select one reviewed discovery backend, provision least-privilege service credentials, verify HTTPS webhook delivery and secret rotation, and define deep-reorg compensation and rate governance. No mainnet connection or deployment occurred.
