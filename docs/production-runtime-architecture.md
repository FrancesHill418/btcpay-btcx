# BTCX production runtime architecture

**Decision:** retain the current BTCX payment discovery and validation topology for production packaging. This is a runtime boundary decision only; it does not authorize production deployment, BTCX mainnet use, or real-fund transactions.

## Required runtime services

| Service | Required? | Responsibility |
|---|---|---|
| BTCPay Server with the BTCX plugin loaded | Yes | Hosts the plugin in-process. The plugin owns BTCX payment handling, wallet operations through RPC, invoice monitoring, payment persistence, and confirmation/reorg reconciliation. BTCPay's invoice lifecycle and store/UI are provided here. |
| Bitcoin-PoCX node with wallet RPC | Yes | Supplies the BTCX chain and the authenticated RPC endpoint used by the plugin. The BTCX receiving wallet is managed by the node's wallet RPC; it does not require a separate wallet server. |
| electrs-btcx | Yes, for the current plugin implementation | Provides Electrum address-script history used by the payment listener to discover candidate payments. It also supplies the Electrum endpoint configured in BTCPay/plugin. |
| PostgreSQL | Required when used by the BTCPay deployment | Persists BTCPay's invoices, stores, settings and payment records. It is a BTCPay platform dependency, not BTCX-specific. If the operator's existing BTCPay deployment already has its required database service, do not provision a second BTCX database. |

The listener is hosted within BTCPay Server; it is not another container. For every monitored invoice, the plugin derives the expected output script from the saved invoice details and queries Electrum history through electrs-btcx. It then uses Bitcoin-PoCX RPC to fetch and validate candidate transactions and outputs, check active-chain ownership and confirmation counts, and reconcile changes such as reorgs. The implementation has no node-RPC-only address-history scanner. Removing electrs-btcx without replacing and validating that discovery path would stop current payment detection.

## Build-time dependencies

### bindex-btcx

**bindex-btcx = build/runtime dependency of electrs, not independent service.** It is integrated into the selected electrs-btcx build and provides the BTCX-compatible indexing behavior electrs uses. Operators run the electrs-btcx service/image; they do not start a separate bindex-btcx container. An alternative electrs/indexer build would need to preserve the address-history behavior required by the plugin and be independently validated.

The current staging Compose also configures electrs to share the Bitcoin-PoCX network namespace because its bindex implementation requests Bitcoin REST through localhost. This is a topology detail of the selected node/indexer build, not a reason to deploy another service.

## Optional services

These may be used by an operator or merchant, but are not required for BTCX payment processing itself:

| Service | Status | Notes |
|---|---|---|
| XBoard | Optional application integration | Runs independently if the merchant uses XBoard. Its Greenfield provider and webhook handling are not part of the BTCPay/BTCX chain runtime. |
| Reverse proxy / TLS terminator | Deployment-dependent | Commonly needed to publish secure BTCPay/XBoard web endpoints, but not part of the BTCX payment listener topology. |
| Monitoring, alerting, log collection, backup orchestration | Operational enhancements | Strongly recommended for a production operator; they are not BTCX protocol or payment-processing services. |
| External rate source | Optional product/configuration choice | The current plugin supports administrator-configured manual BTCX/CNY pricing; no external pricing daemon is needed for that mode. |
| RPC/debug exposure helper | Optional operator tooling | Do not expose node wallet RPC publicly. Use restricted private networking and protected credentials. |

`wallet-init` in the repository's root Compose is a one-shot regtest bootstrap helper. It is not an independent wallet container and is not a required production runtime service. Production wallet creation/loading belongs to the operator's reviewed wallet lifecycle and backup procedures.

## Client-side software

### Phoenix

**Phoenix = client-side wallet, not server dependency.** A payer may use Phoenix or another BTCX-compatible wallet to scan a checkout QR and send BTCX. Phoenix does not run as a BTCPay-side container or server process. The project's protocol fixture is compatible, while real-device QR/payment E2E remains pending in the project state; this document does not claim device verification.

## Confirmed service inventory

For a self-hosted deployment that includes BTCPay's platform dependencies, the runtime inventory is:

1. BTCPay Server (BTCX plugin runs inside it)
2. Bitcoin-PoCX (daemon and wallet RPC in the node service)
3. electrs-btcx (includes/uses bindex-btcx in its build)
4. PostgreSQL, if required by the chosen BTCPay deployment and not already provided externally

There is no separate BTCX wallet container, bindex-btcx container, Phoenix server/container, or listener container. The current stack has no other BTCX-specific runtime service requirement. Optional platform components such as TLS proxy, monitoring, and backup agents depend on the operator's environment.

This decision preserves the current verified regtest discovery path; it does not imply that current development images, patches, wallet custody, mainnet support, or production operations have passed production approval. See [PROJECT-STATE.md](PROJECT-STATE.md) and [production deployment](production-deployment.md) for current gates. The architectural boundary follows [BTCPay's official Altcoin integration model](https://docs.btcpayserver.org/Development/Altcoins/).
