# BTCX payment plugin for BTCPay Server

This repository provides an external BTCPay Server plugin for accepting BTCX on-chain payments. It implements the `BTCX-CHAIN` payment method, invoice-labeled receive addresses through Bitcoin-PoCX wallet RPC, Electrum-based payment discovery through electrs-btcx, confirmation and reorganization reconciliation, and administrator-managed manual BTCX/CNY rates.

## Compatibility and components

- **BTCPay Server v2.4.4** is the pinned and validated integration target. The plugin is external; this project does not modify BTCPay core.
- **BTCX/CNY pricing** uses an administrator-entered manual rate in BTCPay. Invoice creation records an immutable rate and timestamp snapshot.
- **Wallet RPC** is provided by a dedicated Bitcoin-PoCX node wallet. The plugin allocates receive addresses through the node's authenticated wallet RPC.
- **Payment discovery** uses **electrs-btcx**, with **bindex-btcx** built into electrs as its BTCX indexing dependency.
- **XBoard Greenfield integration** is an independent `BtcpayBtcx` payment plugin under [`integrations/xboard/BtcpayBtcx`](integrations/xboard/BtcpayBtcx/README.md). It registers `BTCPayBTCX` and coexists with the untouched XBoard `BTCPay` plugin. XBoard itself is not included or modified here.

## Validation status

The isolated staging/regtest E2E passed for the standalone XBoard `BtcpayBtcx` plugin: BTCPay created a CNY invoice using `BTCX-CHAIN`, a real regtest payment was detected and confirmed, and a signed Greenfield webhook caused the matching XBoard order to reach paid status. A signed duplicate callback returned HTTP 200 without changing the paid order or creating another delivery record. This validates staging only.

BTCX mainnet address allocation is **disabled by default**. It requires the explicit `BTCX:Wallet:AllowMainnet=true` setting and is rejected when the host environment is `Development` or `Staging`. This version still requires manual mainnet acceptance before any production use; staging results do not establish mainnet readiness.

## Staging package

The isolated regtest stack and payment walkthrough are documented in [`integrations/staging/HEALTHCHECKS.md`](integrations/staging/HEALTHCHECKS.md). The stack uses BTCPay Server v2.4.4, Bitcoin-PoCX, electrs-btcx, and PostgreSQL. Do not use staging credentials or regtest instructions for production.

## License

Project-authored source and documentation are licensed under the [MIT License](LICENSE), copyright FrancesHill418. Third-party source and adapted material retain their own licenses and notices; see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). The project MIT license does not replace or relicense third-party components.
