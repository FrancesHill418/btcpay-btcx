# BTCX × BTCPay architecture audit

**Audit basis:** public upstream documentation and default-branch repository pages viewed 2026-09-26. This directory has no usable Git checkout, so its intended BTCPay version, branch, package lock, .NET SDK pin, and plugin implementation cannot be established. Upstream `master` is reference material, not a deployment compatibility promise.

## Findings

BTCPay's current extension model keeps altcoin support outside core. Its current Altcoins guide says to build a plugin extending `BaseBTCPayServerPlugin`; the plugin registers the coin network, payment handler, services, and UI extension points. The official template has a plugin project, a test project, and a BTCPay submodule, and its entry point registers services from `Execute`. The template currently documents .NET SDK 10 or later. The concrete plugin must pin to a stable BTCPay tag and set its `PluginDependency` to that same version before implementation.

The Monero plugin is a useful end-to-end reference for plugin loading, wallet/node RPC configuration, payment callbacks, UI, and deployment integration. It is not a safe BTCX design to copy wholesale: its README warns it uses one shared wallet across stores, which is unsuitable for a multi-tenant production payment receiver. BTCX should keep receiving separate from spending and derive/store per-invoice receiving scripts without putting a hot spending wallet in the invoice path.

## Extension-point map

| Concern | Current BTCPay extension point / reference | BTCX direction |
|---|---|---|
| Plugin entry | `BaseBTCPayServerPlugin`; `Execute(IServiceCollection)`; template `Plugin.cs` | Own assembly and metadata, depend on pinned BTCPay version |
| DI / lifecycle | Services registered from `Execute`; optional hosted services/controllers | Register node/indexer clients, payment handler, listener and health/lifecycle services; no core edits |
| Network/payment method | Altcoins guide: register coin network and payment handler | Implement BTCPay's current payment-method contracts after source checkout; preserve BTCX scripts/amount precision |
| Payment detection | Existing altcoin plugin patterns and BTCPay invoice/payment pipeline | Dedicated listener/state reconciler; idempotency key is outpoint `(txid,vout)` plus network; re-check canonical chain on reorg |
| Wallet | Wallet service/UI belongs in plugin; Monero plugin demonstrates external wallet RPC integration | Initially receive-only; receiving derivation independent from withdrawal/signing |
| Background work | DI hosted services / node notifications where supported | Notifications accelerate; durable cursor + polling/reconciliation recover missed events and restarts |
| Persistence | Plugin-owned EF context/schema and migrations documented by BTCPay | Persist scan cursor, seen outpoints, block anchors, invoice mapping and transitions in plugin schema |
| UI | `IUIExtension` registered in `Execute`; extension points are core-provided | Use only where store configuration or operator visibility is needed |
| Configuration | Plugin options/environment and BTCPay plugin settings | RPC/indexer endpoint, network, confirmation policy and secrets from protected config; never log credentials |
| Webhooks | BTCPay invoice event delivery; Greenfield webhooks are HMAC signed | Let BTCPay own invoice events; XBoard validates signature/auth and deduplicates deliveries |
| BitPay compatibility | BTCPay retains a legacy BitPay-compatible API with limited feature scope; Greenfield is the newer API | Validate whether plugin payment methods and BTCX currency are visible through exact legacy endpoints before committing to XBoard's BitPay adapter |

## Proposed component boundaries

```text
XBoard checkout adapter
  └─ BTCPay API (create/retrieve invoice; checkout URL; authenticated webhook)
      └─ BTCPay invoice and payment state
          └─ BTCX plugin
              ├─ network/address/script + amount rules
              ├─ node RPC adapter (chain/block/raw transaction/broadcast as needed)
              ├─ indexer adapter (script history, UTXO, mempool)
              ├─ durable payment listener and reorg reconciler
              ├─ receive-only derivation service
              └─ plugin persistence/configuration/UI
                  └─ bitcoin-pocx node and BTCX-specific indexer
```

XBoard owns order identity and fulfillment only. It must not scan addresses, call BTCX RPC, or calculate confirmations. The plugin owns chain interpretation and reports observed payments into BTCPay's invoice pipeline. BTCPay owns invoice presentation/status/webhook delivery. These boundaries still require API-level verification in a real BTCPay checkout.

## Current-source constraints

BTCX is not wire-identical to Bitcoin: the current `bitcoin-pocx` network reference specifies a 286-byte PoCX header whose block hash zeroes the 65-byte signature, and unique address HRPs/prefixes. `btcx` is a Rust wallet stack (chain params, keys, seed storage, Electrum client, BDK wallet), not a .NET BTCPay adapter. `electrs-btcx` serves Electrum and uses `bindex-btcx`; it is not the separate Esplora REST service. `bindex-btcx` reads through Bitcoin-PoCX Core REST and explicitly requires Core v31+ with REST enabled. Keep HTTP REST bound to a private network: its documented node endpoint is unauthenticated on the RPC port.

## Recommended implementation order

1. Restore/populate this repository and pin the intended BTCPay stable tag, .NET SDK, template commit and plugin dependency.
2. Re-audit the actual source and current Monero plugin contracts at those revisions.
3. Implement a minimal plugin scaffold and isolated BTCX network/address tests.
4. Select one payment-detection source after local test-environment validation; build durable idempotency/reorg handling before invoice UI.
5. Add invoice and webhook behavior, then validate XBoard compatibility against the actual legacy BitPay API or choose Greenfield if the exact BitPay contract cannot represent BTCX.

## Sources

- [BTCPay altcoin development guide](https://docs.btcpayserver.org/Development/Altcoins/)
- [BTCPay plugin development guide](https://docs.btcpayserver.org/Development/Plugins/)
- [Official plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template)
- [BTCPay Monero plugin](https://github.com/btcpay-monero/btcpayserver-monero-plugin)
- [BTCPay Server repository](https://github.com/btcpayserver/btcpayserver)
- [BTCX wallet stack](https://github.com/PoC-Consortium/btcx)
- [bitcoin-pocx network parameters](https://github.com/PoC-Consortium/bitcoin-pocx/blob/master/docs/6-network-parameters.md)
- [bindex-btcx](https://github.com/PoC-Consortium/bindex-btcx)
- [electrs-btcx](https://github.com/PoC-Consortium/electrs-btcx)
- [esplora-pocx API](https://github.com/PoC-Consortium/esplora-pocx/blob/pocx/API.md)
