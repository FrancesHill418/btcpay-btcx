# BTCX architecture simplification audit

**Audit date:** 2026-09-29  
**Scope:** classification and recommendations only. No code, Compose, or runtime configuration was changed.  
**Architecture authority:** [BTCPay Server: How to add an Altcoin](https://docs.btcpayserver.org/Development/Altcoins/), reviewed 2026-09-29.

## Executive findings

The official minimum is an externally maintained BTCPay plugin plus a Compose fragment that supplies the coin daemon and wallet RPC. The plugin owns payment logic, wallet management, and UI. The guide explicitly says not to modify BTCPay core.

This project follows that boundary: BTCX payment, wallet RPC integration, address allocation, monitoring, confirmation/reorg reconciliation, and UI are in the plugin; Bitcoin-PoCX and its wallet RPC are supplied as a separate service; BTCPay core is not modified. PostgreSQL is part of the BTCPay platform, not a BTCX-specific database.

The current implementation has an additional, concrete dependency: `BtcxPaymentListener` uses Electrum script-history discovery as its only discovery source. The node RPC then verifies candidate transactions, outputs, active-chain membership, and confirmations. Consequently, **electrs-btcx is required for the current implementation**, although it is not required by the official altcoin architecture itself. `bindex-btcx` is used inside the electrs build; it is not a separate Compose service.

## Classification

| Component / concern | Class | Current role and assessment |
|---|---|---|
| BTCPay BTCX plugin | **A — official minimum** | Registers BTCX payment method/rate/UI and implements address allocation, payment observation, and payment state updates. This is the coin integration boundary. |
| Bitcoin-PoCX daemon and wallet RPC | **A — official minimum** | The coin service exposes authenticated RPC to BTCPay. The current node image includes both daemon and wallet RPC; they are not separate services. |
| BTCX RPC client | **B — BTCX-specific requirement** | Plugin-side adapter for network/genesis validation, wallet address creation, transaction/output lookup, block identity, and confirmation data. This is plugin code, not an independent service/container. |
| BTCX receiving wallet | **A/B — official responsibility, BTCX implementation** | BTCPay's model assigns wallet management to the plugin. This implementation uses a dedicated named wallet inside the Bitcoin-PoCX node and allocates invoice-labeled addresses over wallet RPC. No separate wallet daemon is present or needed. This is a spend-capable node wallet, not watch-only/xpub. |
| Payment listener | **B — BTCX-specific requirement** | Hosted inside the plugin/BTCPay process. It polls monitored BTCX invoices and feeds verified outputs into BTCPay's payment services; no listener container is needed. |
| Confirmation policy | **B — BTCX-specific logic using BTCPay lifecycle** | The plugin maps BTCPay speed policy to required confirmations (including 6 for LowSpeed) and updates BTCPay payment status. It is code in the plugin, not a separate service. |
| Reorg reconciliation | **B — BTCX-specific logic using BTCPay lifecycle** | Electrum history is re-read; candidate transactions and canonical-chain details are checked against node RPC; disappeared outputs can be marked unaccounted and payments reconciled. No separate reorg service/container is needed. Reorgs after fulfillment still require merchant policy. |
| electrs-btcx | **B in current design; C only after replacement** | Separate Electrum indexer service. The current listener depends on its script-history API for payment discovery. It can only become optional if a replacement discovery path is implemented and validated. |
| bindex-btcx | **B in current electrs build; E as a separate service** | Electrs uses this indexer implementation for PoCX compatibility. It is not a separately deployed container in the current Compose file. Do not model or add a distinct bindex service. Replacing it requires a compatible electrs/indexer build. |
| PostgreSQL | **A/platform infrastructure** | Needed by this BTCPay deployment to persist invoices/plugin-related BTCPay state, but it is not a BTCX-specific dependency. In a hosted/existing BTCPay deployment, this is already part of the platform. The official guide also treats PostgreSQL as a base dependency in local development setups. |
| Wallet RPC endpoint/credentials | **A/B — integration boundary and security configuration** | The plugin connects to the private node wallet RPC using the configured authenticated mechanism (cookie or credentials). It is an interface/configuration, not another container. Keep it private and secret-backed. |
| `wallet-init` | **D — environment/bootstrap helper** | Current one-shot service initializes/loads the regtest wallet. It is not an independent wallet or a production runtime requirement if operators provision and load the production wallet through an approved lifecycle. |
| Phoenix | **D for validation; external customer wallet at runtime** | Phoenix is a user's wallet application, not a BTCPay-side service. Protocol compatibility was checked, but the project state says real-device E2E is still pending. No Phoenix container belongs in the coin deployment fragment. |
| XBoard provider | **C — optional merchant application integration** | Creates BTCPay invoices through Greenfield and handles authenticated webhooks/order fulfillment. It is outside the altcoin plugin and coin-node minimum; deploy it independently if the merchant uses XBoard. |
| Regtest/mock time/faucet or forge setup | **D — testing-only** | Test-chain facilities and mock-time/forging setup are not production dependencies. Keep them out of production Compose fragments. |
| Combined staging Compose | **E — deployment-role coupling, not necessarily useless components** | The root Compose bundles BTCPay, PostgreSQL, node, wallet initializer, and electrs in one regtest-oriented stack. That is convenient for isolated acceptance but mixes the BTCPay platform, coin fragment, and test bootstrap. Separate deployment artifacts would make the production boundary clearer; this audit does not perform that refactor. |

## Answers

### 1. What containers are minimally required for production receiving?

There are two useful meanings of “minimum”:

* **Official architecture minimum for a new altcoin integration:** the BTCPay host running the plugin plus a coin daemon/wallet-RPC container. PostgreSQL and other BTCPay base services belong to the BTCPay platform.
* **Minimum for this implementation as it exists today:** BTCPay (with the plugin), the Bitcoin-PoCX node/wallet-RPC service, and electrs-btcx. PostgreSQL is also required by this self-hosted BTCPay stack. There is no separate wallet container, listener container, or bindex container. `wallet-init` is currently a regtest bootstrap helper, not an inherent production receiving component.

### 2. Is electrs-btcx required?

**For the official plugin model: no. For the current BTCX implementation: yes.** The listener explicitly uses Electrum as its only script-history discovery source. A future design could replace it with a node-native block/transaction scanner or another validated indexer, but that would be implementation work and is not part of this audit.

### 3. Is bindex-btcx required?

The current electrs-btcx build uses bindex-btcx, so it is required **within that selected indexer build**. It is not a separate service or container. The architecture could use a compatible alternative indexer, but the replacement would need the same required history behavior and PoCX compatibility.

### 4. Is a separate wallet container required?

No. Current receiving wallet management is provided by Bitcoin-PoCX wallet RPC within the node service. Plugin RPC calls load/use the configured named wallet and create/recover invoice-labeled addresses. A separate wallet container would duplicate the current RPC role without addressing a present requirement.

### 5. Can the plugin use Bitcoin-PoCX RPC directly for complete receiving?

It already uses node wallet RPC directly to validate the network, allocate the receive address, and inspect transactions/outputs/confirmations. However, **it cannot perform the complete current payment-detection flow using node RPC alone**: the current listener has no RPC-based address-history/block scan and depends on Electrum history from electrs. Node RPC is the validation and chain-state source after Electrum discovers candidate transactions.

### 6. Which components can be optional enhancements?

* **XBoard provider/XBoard:** optional to BTCX receiving; needed only for that store/order integration.
* **Phoenix:** external payer wallet, not deployment infrastructure; any claim of end-to-end device compatibility remains pending until real-device validation.
* **electrs/bindex:** optional to the official abstract model, but not optional to the code as currently implemented. They can be removed only after a replacement discovery design is implemented and validated.
* Monitoring, external backup orchestration, reverse proxy/TLS, and operator wallet CLI wrappers support production operations but are not BTCX payment-processing containers mandated by the plugin model.

### 7. Is the current `docker-compose.yml` overcomplicated?

It is **broader than a production coin-only fragment**, because it combines a self-hosted BTCPay base stack (PostgreSQL and BTCPay), coin infrastructure (Bitcoin-PoCX and electrs), and regtest wallet initialization. The breadth is justified for a one-command isolated staging environment and is not evidence that all services are needless.

The main simplification opportunity is to keep distinct deployment artifacts/roles: (1) the BTCX coin fragment, (2) the operator's BTCPay base stack, and (3) regtest/test bootstrap. Retain electrs in the current production candidate topology until code has a proven alternate discovery mechanism. Avoid adding a standalone bindex or wallet container. This is a proposal only; no Compose changes were made.

### 8. Does the project conform to BTCPay's official Altcoin plugin model?

**Yes at the architectural boundary; with a wider-than-minimum coin infrastructure footprint.** BTCX functionality is implemented as an external plugin, the coin daemon/wallet RPC is separate from BTCPay core, and no BTCPay core changes are present. The project has the two architectural pieces described by the guide: plugin and Compose-provided coin infrastructure. The current electrs dependency is a BTCX-specific design choice layered onto that model, not a violation of it. The root Compose is a comprehensive staging harness rather than a minimal official production fragment.

## Simplification proposal (not executed)

1. Preserve the existing plugin, payment listener, confirmation and reorg behavior.
2. Keep a production coin fragment for Bitcoin-PoCX daemon + wallet RPC, plus electrs-btcx while the listener requires Electrum.
3. Treat bindex-btcx as an electrs image/build dependency, not a Compose service.
4. Treat BTCPay and PostgreSQL as the platform/base deployment, not BTCX-only infrastructure.
5. Keep wallet initialization/regtest controls in the test harness; document an operator-managed production wallet load/backup procedure separately.
6. Keep XBoard independently deployable and Phoenix outside server-side Compose.
7. Consider replacing Electrum discovery with a node RPC scanner only as a separately scoped, fully validated future change. Do not remove the currently verified discovery path based on this audit.

## Evidence and limits

This classification was checked against the current root `docker-compose.yml`, `docs/PROJECT-STATE.md`, and the plugin's `BtcxPaymentListener`, `BtcxPaymentReconciler`, `BtcxReceiveAddressProvider`, `BtcxRpcClient`, and `BtcxPaymentServiceSink` implementations. Project state records the regtest payment/XBoard E2E as passed and Phoenix real-device E2E as pending. The audit does not certify mainnet support, production readiness, wallet custody design, or the operational suitability of the development PoCX/electrs patch.

The official guide describes the high-level plugin/Compose boundary and calls out payment logic, wallet management, UI, daemon plus wallet RPC, and passing RPC URIs into BTCPay. It also distinguishes the local development Compose (including base services) from the production Docker fragment. See [BTCPay's official Altcoin development guide](https://docs.btcpayserver.org/Development/Altcoins/).
