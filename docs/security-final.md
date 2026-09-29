# Security and reliability review

**Historical review:** this report covered the superseded patch-based XBoard provider. The standalone BtcpayBtcx implementation and its staging E2E are documented in [`integrations/xboard/BtcpayBtcx/README.md`](../integrations/xboard/BtcpayBtcx/README.md) and [`staging-deployment.md`](staging-deployment.md). The historical findings below are not a review of production readiness.

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
- XBoard's provider patch binds one invoice to one exact order and checks signed event, invoice status, metadata, CNY amount, BTCX payment, checkout policy and delivery identity before returning its order mapping. Provider tests cover duplicate delivery; XBoard's existing order-paid path is the final order idempotency boundary. The API token and HMAC secret use configuration values, and provider error messages do not echo them. The later live exact-payment callback is documented below; real duplicate redelivery was not separately exercised.
- Manual BTCX/CNY rate validation is positive, decimal-safe, administrator-controlled, and snapshotted on each invoice. Editing the rate affects only new invoices.

## Verified limits and remaining release gates

- Initial electrs-btcx startup against unmodified PoCX v30.2.1 failed because `/rest/blockpart/<genesis>.bin?offset=0&size=491` returned HTTP 404. A development-only backport of Bitcoin Core PR #33657, adapted to v30's API, is preserved under `integrations/electrs-btcx`. No consensus rules were changed and no RPC fallback listener was added. The isolated patched node and pinned electrs-btcx passed real address/history/UTXO/mempool/transaction/confirmation checks and the BTCPay invoice smoke. Unpatched upstream compatibility remains an operational requirement. See [milestone-06-development-acceptance.md](milestone-06-development-acceptance.md).
- The gated BTCPay payment lifecycle passed with real node/wallet RPC and real electrs discovery; payment settled after six confirmations and moved back to processing then settled after a real invalidate/reconsider reorg cycle.
- Phoenix v2.4.0 parser fixture is **PROTOCOL COMPATIBLE**. Real device/QR/send/receive E2E remains pending.
- XBoard provider tests pass with in-memory SQLite and HTTP fakes; a live isolated Greenfield callback and XBoard order-completion E2E also passed as recorded below.
- The manual rate is administrator-entered. No trustworthy native BTCX/CNY market source has been approved.
- Before any manual mainnet review, replace/review the spend-capable node wallet authority, complete encrypted/offline backup and restore drills, upstream or operationally pin the PoCX/electrs compatibility patch, provision least-privilege service credentials, verify HTTPS webhook delivery and secret rotation, and define deep-reorg compensation and rate governance. Phoenix device E2E remains unverified and must be closed or explicitly accepted in that review. No mainnet connection or deployment occurred.

## Final development recheck (2026-09-28)

The real electrs-backed BTCPay smoke ran with isolated PostgreSQL on loopback, loopback-only node RPC/REST, a private node cookie, and an electrs service bound to loopback. The payment test verified the stored invoice's CNY/BTCX quote snapshot and listener behavior against actual Electrum history. No production credentials, wallet seed, private key, public RPC endpoint, or mainnet funds were used. At that checkpoint, XBoard live and Phoenix device flows were not yet available; the later live XBoard flow and remaining Phoenix limitation are recorded below.

## Final E2E security delta (2026-09-28)

The later XBoard run supersedes the earlier statement that live callback/order completion was unavailable. In a development-only loopback environment, a real BTCX regtest payment settled a BTCPay invoice, the actual `InvoiceSettled` delivery reached the XBoard HTTP callback with HTTP 200, and the bound order was marked paid. The provider checks the HMAC-SHA256 signature over the raw request body with `hash_equals`, binds invoice ID/store/order/CNY amount/BTCX method and settled payment state, and records delivery identity idempotently. No fake webhook or production credential was used. The BTCPay delivery record confirms HTTP 200; a separate BTCPay-triggered duplicate redelivery was not run. Underpayment, overpayment and expiry were not live-validated in the XBoard environment; provider tests remain their alternative coverage.

Phoenix remains **PROTOCOL COMPATIBLE**, not **REAL DEVICE E2E VERIFIED**. There is no attached device (`/dev/bus/usb` absent), `adb` and Flutter are not installed, and no emulator runtime is configured. This is the remaining blocker for final E2E completion. No production service, BTCX mainnet or mainnet funds were accessed.

## RC1 production hardening follow-up (2026-09-29)

| Control | Review result | Evidence / remaining scope |
|---|---|---|
| RPC exposure | **Code PASS / production network unverified** | Plugin rejects public RPC/Electrum endpoints; staging Compose backend is internal, and REST/RPC ports are not published. Node cookie still provides wallet RPC authority. No production firewall or Cloudflare/tunnel route exists in this checkout. |
| Mainnet gate | **PASS (configuration test only)** | Mainnet parameters and generic address/RPC/reconciliation paths exist. `BTCX:Wallet:AllowMainnet` now defaults false; simulated RPC tests confirm default refusal and explicit opt-in address allocation. No mainnet connection/test occurred; independent code/security review is required. |
| Wallet custody/persistence | **BLOCKED for production** | Node wallet is spend-capable; Compose uses persistent node data and a cookie volume. Staging backup recovered labels/transactions. No production wallet was created; no production backup/restore or signing boundary was reviewed. |
| BTCPay/Postgres/XBoard secrets | **Partial** | Staging Postgres secret uses a file. RPC cookie is mounted read-only. XBoard patch reads Greenfield token/HMAC from read-only `/run/secrets` paths and persists only file paths; patch applies at pinned XBoard commit and isolated PHPUnit passes 7/28 including path allowlisting. Production secret mounts, ACLs, rotation and full staging E2E using this patch remain unverified. |
| Webhook signature/replay | **Code/test PASS; production route unverified** | Provider validates raw-body HMAC in constant time, event type, invoice/store/order/amount/method/state and delivery identity. Automated duplicate delivery test passes. A duplicate callback was not re-delivered through the live BTCPay→XBoard route after the secret-file change. |
| Duplicate/underpayment/overpayment/expiry | **Automated fixture PASS; live negatives unverified** | XBoard provider tests cover duplicate delivery, underpayment, overpayment and expired invoice rejection. The successful exact-value payment was live regtest. No live failure-case transaction was sent. |
| Reorg | **Regtest PASS / business policy open** | Prior gated regtest invalidation moved the payment back to Processing and reconsideration settled it again. BTCPay aggregate Settled can remain sticky; already-fulfilled XBoard order compensation still needs an approved operator/accounting process. |
| Backups/database restore | **Partial** | Development wallet `backupwallet` restore recovered 9 labels and 5 transaction IDs. A production-encrypted wallet drill and coordinated BTCPay/PostgreSQL/XBoard DB restore were not run. |
| TLS / Cloudflare / Tunnel | **BLOCKED / not deployed** | Staging used isolated loopback/private HTTP. No public certificate, reverse proxy, Cloudflare proxy/tunnel or production webhook TLS path was created or examined. Production must test origin TLS, ingress allow-list, no RPC/Electrum route, caching bypass, WebSockets, source IP handling and fail-closed behavior. |
| Images/dependencies | **Partial** | Staging Compose and Dockerfile base references are digest-qualified and recorded in [image-lock.md](image-lock.md). Custom images are local-only; one running PoCX container differs from the currently tagged image. Dockerfiles still use live apt package indexes and unpinned packages; production artifacts are unpublished. |

This follow-up changes neither the earlier staging E2E evidence nor its scope. The XBoard mounted-secret provider patch has not yet received a full staging invoice/payment/webhook rerun. No production configuration, service, wallet, credential, mainnet node or funds were accessed.
