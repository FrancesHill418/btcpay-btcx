# Production deployment candidate

**Status: NOT APPROVED FOR PRODUCTION.** This is a release preparation and future deployment control document, not an executable mainnet procedure. Do not deploy, connect to BTCX mainnet, accept customer payments, or use mainnet funds from this candidate. The successful staging E2E used regtest only.

## Release baseline and dependency lock status

The repository currently gives these source revisions for the tested staging candidate:

| Component | Exact source/version | Production status |
|---|---|---|
| BTCX BTCPay plugin | `0.1.0`, source is the release commit; .NET target `net10.0`, SDK `10.0.401` | Network/address/RPC/payment paths support mainnet parameters; explicit `BTCX:Wallet:AllowMainnet` gate defaults false; opt-in covered only with mocked RPC |
| BTCPay Server | `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` | Staging validated only |
| Bitcoin-PoCX | commit `005bf0098e217b76a2627bfae458dff4f5718dd5`; bundled Bitcoin source commit `b88b852644f629cd5f25b3424d11b462462c24b3` | Development regtest only; compatibility patches are not approved for production |
| bindex-btcx | commit `eda7c70660baa06affef464c7ea1e131c39304f1` | Staging only |
| electrs-btcx | `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` | Staging only |
| XBoard | commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` plus `integrations/xboard/0001-btcpay-btcx-provider.patch` | Isolated E2E revision; production packaging/security approval outstanding |
| PostgreSQL | `16.15-alpine3.24`, OCI index `sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea` | Pinned in candidate staging Compose; production registry artifact/version not approved |
| .NET SDK / BTCPay container | `10.0.401` / `2.4.4` plus OCI index digests in [image-lock.md](image-lock.md) | Base images pinned; live apt package installs and final production registry artifacts remain open |
| Node / indexer build OS packages | Ubuntu `24.04`; Debian `trixie` / `trixie-slim`; apt installs unversioned packages | Not version-locked or reproducible |

Local image IDs returned by `docker image inspect` at preparation time (not registry manifest digests or production approvals) are:

* PostgreSQL staging image: `sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea`
* BTCPay staging image: `sha256:34381e062bcaf1f733230f7d46dca28b8948acc6a0ce91dfdc4ee5d4e297ec0e`
* Bitcoin-PoCX staging image: `sha256:e84901f03d3d70e89f798235255523201c1be5a9e5756259e54984d7de0e9d99`
* electrs-btcx staging image: `sha256:31959be4c1ccc92aa65487b7800fe0786a4990b1ab33189743a2bad82e3969f2`

These identify local images and development/regtest content, **not production image approvals**. A production lock is incomplete until the approved source revisions, every base/runtime image OCI manifest digest, OS package snapshot, NuGet/Cargo/PHP dependency lock, build toolchain, XBoard patch checksum, and produced artifact checksums are independently reviewed and recorded. Base image tags are digest-qualified, but the current Dockerfiles still install unpinned apt packages from live repositories, so the requirement to fix all production dependency versions is not yet met. Do not infer approval from this table or promote the running staging containers.

## Production gates (all must close)

1. Review the mainnet address/network/RPC/payment path and explicit config gate. `BTCX:Wallet:AllowMainnet` defaults to false; environment form is `BTCX__Wallet__AllowMainnet=false`. Only an independently authorized deployment may deliberately set it true. The opt-in path has mocked RPC coverage only; no mainnet connection or transaction was made.
2. Approve supported Bitcoin-PoCX / bindex / electrs compatibility. Current validated node carries a development-only REST compatibility backport; validate an upstream-supported implementation and independent release artifacts.
3. Complete Phoenix real-device QR/send/receive E2E on a safe non-mainnet network.
4. Approve a receive-key design, least-privilege node access and recovery rehearsal. The current plugin calls node wallet RPC to allocate addresses; the cookie has wallet authority and the wallet is spend-capable. Watch-only/xpub allocation is not implemented.
5. Approve manual-rate governance, including authorized operators, dual control, rate freshness, order cut-off, audit, and reconciliation. There is no approved live BTCX/CNY market source.
6. Lock all dependencies with immutable digests and reproducible build inputs; scan and attest the artifacts.
7. Review secret delivery/rotation. The current provider patch reads the Greenfield token and webhook HMAC from read-only mounted secret files and stores only file paths in XBoard configuration. Production Docker secret/secret-manager mounts, ACLs, rotation and restore drills remain to be wired and verified; the full staging E2E has not been rerun with this patch.
8. Provision separate production BTCPay and XBoard services, databases, credentials, wallets, webhook secrets, TLS, firewall policy, monitoring, alerting, retention and incident response.
9. Approve confirmation and deep-reorg business handling. BTCPay invoice aggregate state may remain sticky `Settled` after a reorg while the individual payment rolls back; an already fulfilled XBoard order needs manual compensation.
10. Complete signed security, privacy, legal/accounting, production-equivalent backup/restore, cutover and rollback reviews. Phoenix real-device validation is still outstanding and requires an explicit gate decision.

## Target topology and trust boundaries

After the gates are closed, deploy independently managed private services: BTCX node, address-allocation boundary, bindex/electrs, BTCPay, PostgreSQL and XBoard. Only the public BTCPay checkout and XBoard HTTPS endpoints should be internet reachable through reviewed TLS reverse proxies. Keep node RPC and Electrum private and firewall-restricted. Do not reuse `docker-compose.yml`, Compose project, network, volumes, database, wallet, RPC identity, API token or webhook secret from staging. The root Compose file is a regtest acceptance environment and includes the development compatibility patches.

The plugin's current RPC client supports node cookie file or username/password (choose exactly one); a read-only mounted cookie path is used in staging. Cookie authentication does not make a spend-capable wallet read-only. Require file-based secret injection from an approved secret manager/orchestrator, restrictive ownership/mode, rotation and log redaction. Never put secret values in `.env`, shell history, CI logs, source control, images or support tickets.

The candidate variable names and deliberately non-secret endpoints are in [`../integrations/production/.env.example`](../integrations/production/.env.example). It is documentation only. The mainnet gate stays false in this candidate; do not flip it during this release preparation.

## Store setup and invoice policy

In each production BTCPay store, enable only `BTCX-CHAIN`. The rate rule is `BTCX_CNY = manualbtcx(BTCX_CNY)`. A separately authorized operator enters `1 BTCX = X CNY` in the plugin's server BTCX settings after recording the source, effective timestamp, approver and expiry/check time in the approved accounting record. Disable BTCX invoice creation if the quote is stale, disputed, invalid, or has no authorized operator. Test this process on an isolated non-mainnet environment before cutover.

Each invoice captures a quote snapshot (fiat amount/currency, BTCX amount/currency, rate, source, timestamp). An authorized rate edit applies only to invoices created afterward; an already-created invoice retains the original amount and quote through its displayed expiry. Do not silently reprice or extend an invoice. If a customer must receive a new quote, expire/cancel the old invoice through supported BTCPay workflow, create a new invoice, and preserve both audit records. Define the production expiry/freshness relationship before enabling orders.

## Confirmation and reorg policy

The plugin maps BTCPay speed policies to BTCX confirmations as follows: HighSpeed is 0 confirmations (1 when the transaction signals RBF), MediumSpeed 1, LowMediumSpeed 2, and LowSpeed 6. The validated XBoard provider selects LowSpeed and zero underpayment tolerance; the staging positive payment settled at six confirmations. Production must retain or deliberately revise this only through reviewed configuration/code and an explicit risk approval. Never treat mempool observation as fulfillment approval.

On reorg or a payment rollback: suspend fulfillment for affected invoices, compare node canonical chain with indexer and plugin payment rows, preserve logs and webhook deliveries, and have a payment operator classify re-mined, displaced and absent outputs. The payment can return to Processing even though the BTCPay aggregate invoice remains `Settled`. XBoard validates the underlying payment status before new paid-order mapping, but cannot reverse an order already fulfilled. Open an incident and apply the pre-approved manual refund/credit/fulfillment compensation; do not edit database rows or replay a fake callback to conceal the event.

## HTTPS and webhook configuration

Publish BTCPay and XBoard only behind valid publicly trusted HTTPS with automated certificate renewal, TLS 1.2+ (or current organization policy), HSTS where appropriate, request-size/rate limits and verified external certificate renewal. Configure BTCPay's webhook target to the XBoard HTTPS notification endpoint and subscribe only to `InvoiceSettled`. Use a production-only Greenfield token scoped to the one store and required invoice/webhook operations, and an independent random HMAC secret. XBoard must validate the raw-body signature in constant time, event type, invoice ID, store ID, metadata order ID, CNY amount/currency, BTCX payment method, settled state, and payment amount before idempotent order fulfillment. Verify real signed delivery, retry and duplicate behavior in a production-equivalent non-mainnet rehearsal; do not use fake callbacks as acceptance evidence.

## Backup and recovery

Follow [`wallet-backup-recovery.md`](wallet-backup-recovery.md). PostgreSQL, BTCPay state, XBoard state, wallet backup and release/config metadata require encrypted access-controlled backups. Secret-manager recovery is independent and must be tested. electrs/bindex indexes are rebuildable from a compatible complete node chain; preserve their exact version/configuration and budget for a full reindex. Never restore staging data or credentials over production. Never use `docker compose down -v` as a recovery step.

## Change and cutover record

Use [`production-security-checklist.md`](production-security-checklist.md) and [`production-cutover.md`](production-cutover.md). A future deployment requires a separate explicit production authorization after every gate has evidence and named approval. This document and the RC tag do not authorize deployment. No production resources were contacted or changed while preparing this candidate.
