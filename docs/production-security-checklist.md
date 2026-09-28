# Production security checklist

**Current result: NO-GO.** Mark an item complete only with a dated evidence link, reviewer and owner. This list is for a future production review; no production environment was configured during release preparation.

## Release and dependencies

- [ ] Mainnet support is deliberately implemented, reviewed, and covered by safe-network tests. Current plugin rejects `Network=main`.
- [ ] Phoenix real-device QR, send and receive flow passes on a non-mainnet network.
- [ ] Bitcoin-PoCX / bindex / electrs compatibility is based on a supported reviewed release. The staging REST compatibility patch is development-only.
- [ ] Every source commit, base/runtime OCI digest, OS package, NuGet/Cargo/PHP lock, build tool and output artifact checksum is immutable and reviewed. Base image references are digest-qualified, but apt packages use live unpinned repositories and plugin transitive NuGet dependencies have no committed lock file.
- [ ] Build is reproducible from the documented source and lock manifest; SBOM, vulnerability review and artifact provenance are archived.
- [ ] No BTCPay core changes are included; XBoard provider patch is reviewed against the exact XBoard commit.

## Environment separation and secrets

- [ ] Production and staging use different BTCX wallets, wallet backup keys, RPC identities/cookies, RPC ACLs, databases, Greenfield API keys, XBoard API configuration and webhook HMAC secrets.
- [ ] Production has separate service accounts, DNS, networks, hosts, volumes, TLS keys, monitoring, backup targets and secret-manager namespaces.
- [ ] No staging database/volume, wallet, API token, webhook HMAC, RPC cookie or customer data is copied into production.
- [ ] Secrets are issued by an approved secret manager/orchestrator as files or equivalent protected workload identity; they are absent from Git, `.env` values, container image layers, command lines, shell history, logs, crash dumps and support bundles.
- [ ] BTCPay Greenfield token has only the required store-scoped invoice and webhook permissions; token rotation/revocation is rehearsed.
- [x] Provider patch reads the Greenfield token and webhook HMAC from read-only mounted secret files; only file paths are persisted in provider configuration. Secret paths are restricted to `/run/secrets/` outside tests. `git apply --unidiff-zero --check` was verified at the exact XBoard commit and the isolated suite passed 7 tests / 28 assertions. Production secret mounts, ACLs, rotation, recovery and full staging E2E with mounted secrets remain unverified.
- [ ] PostgreSQL credentials are independently generated and rotated; DB is private, encrypted at rest and backed up through a protected process.
- [ ] RPC authentication selects exactly one supported mechanism (cookie or username/password). Cookie exposure and wallet RPC authority are reviewed; no node datadir is mounted into BTCPay.
- [ ] Logs and telemetry redact credentials, customer-sensitive invoice data and payment payloads according to retention policy.

## BTCX wallet and RPC

- [ ] Dedicated production receiving wallet is created only on the verified production node/network using the approved key-custody procedure; its name/network/genesis are verified before use.
- [ ] Wallet is not shared with staging, operator personal wallets, treasury hot wallets or another merchant environment.
- [ ] Address allocation uses an approved restricted service/key boundary. Current plugin uses spend-capable node wallet RPC for `getnewaddress` and `getaddressesbylabel`; watch-only/xpub allocation is not implemented.
- [ ] Node RPC binds only to a private interface; firewall allows only required BTCPay/address service peers; RPC and Electrum ports are not internet-published.
- [ ] Authentication material has least privilege, file mode/owner, rotation, revocation, monitoring, and an incident response procedure. Confirm cookie rotation behavior after node restart.
- [ ] Application and node verify expected network and genesis; wrong-network startup fails closed.
- [ ] RPC cookie rotation and receiving-wallet rotation have separate reviewed procedures; wallet rotation preserves old invoice reconciliation and requires dual control for any fund movement.

## HTTPS, Greenfield and webhook

- [ ] BTCPay checkout and XBoard callback use public HTTPS with valid renewed certificates; no production HTTP exception is configured.
- [ ] TLS termination, proxy trust headers, host allow-list, HSTS, ingress firewall, request limits and external renewal checks are reviewed.
- [ ] If Cloudflare or a tunnel is used, origin TLS is Full (strict) or equivalent, DNS/proxy routes expose only BTCPay checkout and XBoard HTTPS callback, and no RPC/Electrum/admin route is proxied or tunneled. Origin access is firewall-restricted to the proxy/tunnel; webhook delivery, client IP handling, WebSocket behavior, caching bypass and fail-closed tunnel outage behavior are tested.
- [ ] XBoard Greenfield URL is the production BTCPay hostname, store ID is production-only, and token scope is minimal.
- [ ] Only `InvoiceSettled` is subscribed for paid fulfillment; XBoard validates raw-body HMAC in constant time and validates event type, store, invoice, order metadata, CNY amount/currency, BTCX method, actual payment and invoice/payment state.
- [ ] Webhook retries, duplicate delivery, key rotation, replay, wrong signature and unavailable endpoint are exercised with real signed test deliveries on an isolated non-mainnet rehearsal.
- [ ] Order completion is idempotent and delivery IDs/invoice IDs are retained in audit records without retaining secrets.

## Rate, invoices, confirmations and reorgs

- [ ] Authorized rate operators, dual control, source evidence, validity window, escalation and accounting reconciliation are approved.
- [ ] Only the manual `BTCX_CNY` rule is active unless another reviewed source is implemented; invalid/stale/missing rates block new BTCX invoices.
- [ ] Each invoice quote snapshot is retained. Rate changes apply only to newly created invoices; existing invoices retain their original amount until expiry and are not silently repriced.
- [ ] Expiry/reissue procedure preserves links between old/new invoice and order; customer support wording is approved.
- [ ] Confirmation risk is approved: LowSpeed corresponds to six confirmations in this plugin; XBoard currently selects LowSpeed and zero underpayment tolerance.
- [ ] Mempool or zero-confirmation payment cannot fulfill an order under the approved production policy.
- [ ] Reorg monitoring compares canonical node state and indexer observations. Deep reorg incident owner, customer contact, ledger correction and already-fulfilled order compensation are documented.
- [ ] Operators understand BTCPay aggregate invoice status may remain `Settled` after a reorg while an individual payment rolls back; database edits and synthetic callbacks are prohibited.

## Recovery, monitoring and approval

- [ ] PostgreSQL, BTCPay/XBoard databases, wallet, node data, TLS/config metadata and secret-manager recovery have distinct protected backups and retention policies.
- [ ] Restore drill proves invoice/order binding, wallet receive labels, node/indexer agreement and reconciliation after restore.
- [ ] electrs/bindex full rebuild procedure and expected recovery time/capacity are measured for production chain scale.
- [ ] Alerts cover node/RPC unreachable, wallet unavailable, indexer lag/divergence, webhook failure/retries, stale rate, invoice processing age, disk space, backup age and reorgs.
- [ ] Incident response, contact tree, audit retention, access reviews, dependency patch cadence and rollback are approved.
- [ ] Named security, operations, finance/accounting and product owners sign off. Production cutover remains a separate explicitly authorized change.
