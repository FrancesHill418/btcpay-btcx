# BTCX implementation map (TASK 03 handoff)

Audit baseline: BTCPay `v2.4.4` / `2d5a0d8077bb33af080e949031da33d84b80638d`; plugin source in this checkout. References point to the pinned BTCPay source where possible. This is a source map, not a claim that the planned listener/address flow exists.

## Existing BTCX plugin and tests

| Component | Current source | Current interface/state | Planned extension |
|---|---|---|---|
| Entry point/DI | `src/BTCPayServer.Plugins.BTCX/Plugin.cs` | `Plugin : BaseBTCPayServerPlugin`; `Execute(IServiceCollection)` adds network metadata, currency, settings, rate/payment/link/checkout services | Add address, RPC, discovery and hosted reconciliation services; keep failure isolated from BTCPay startup |
| Network marker | `BTCXNetwork.cs` | `BTCPayNetworkBase`; metadata, BTCX code/divisibility and rate rule only | Add explicit PoCX network identity/params abstraction; do not pretend it is Bitcoin/NBXplorer-compatible |
| Handler | `Payments/BtcxPaymentMethodHandler.cs` | `IPaymentMethodHandler`; rate prompt setup and `BtcxInvoiceSnapshot` prompt details | Allocate address before save; set destination and tracked destination; robust snapshot parser; implement prompt payment data |
| Snapshot | `Payments/BtcxInvoiceSnapshot.cs` | fiat/crypto amounts, currencies, rate, source, timestamp | Keep immutable; extend only with versioned identifiers needed for address/script/network and audit, not mutable market values |
| Link | `Payments/BtcxPaymentLinkExtension.cs` | `IPaymentLinkExtension`, currently returns null | Generate canonical Phoenix `btcx:` URI with exact fixed-decimal BTCX amount |
| Checkout | `Payments/BtcxCheckoutModelExtension.cs` | `ICheckoutModelExtension`, currently skeleton | Display address, amount, URI/QR, pending/confirmation state without secrets |
| Rate | `Rates/ManualBtcxRateProvider.cs` | `IContextualRateProvider` | Preserve per-request latest settings and immutable invoice rate; reject missing/invalid rate |
| Settings | `Rates/ManualBtcxRateSettings.cs`, `ManualRateSettingsService.cs`, `Controllers/BtcxSettingsController.cs`, `Views/BtcxSettings/Index.cshtml` | manual enabled/rate/source/updatedAt settings | Add safe operational controls only if required; no keys in general settings |
| Amount | `Rates/BtcxAmountCalculator.cs` | decimal calculation and rounding helper | Ensure conversion to integer 10^8 atomic units is explicit and validated at payment boundary |
| Unit tests | `tests/BTCPayServer.Plugins.BTCX.Tests/ManualBtcxRateTests.cs` | manual rate/calculation/snapshot/settings coverage | Add network/script/URI/serialization/idempotency/state transition suites |
| Runtime tests | `tests/BTCPayServer.Plugins.BTCX.Tests/RuntimeSmokeTests.cs` | gated BTCPay host/plugin/rate/Greenfield smoke | Extend only after serializer blocker is fixed; then checkout payment link and listener/regtest tests |

## BTCPay v2.4.4 extension points

| BTCPay extension point | Pinned source / class | Interface/API | Existing example | BTCX implementation plan |
|---|---|---|---|---|
| Plugin loading | `BTCPayServer/Plugins/BaseBTCPayServerPlugin.cs`; `BTCPayServer/Plugins/PluginManager.cs` | `BaseBTCPayServerPlugin`, `Execute(IServiceCollection)` | Existing BTCX `Plugin` | Continue ordinary plugin DI; never patch core |
| Network metadata registration | `BTCPayServer.Common/BTCPayNetwork.cs` and `BTCPayServer/Hosting/BTCPayServerServices.cs` | `BTCPayNetworkBase`, `AddBTCPayNetwork(BTCPayNetworkBase)` | BTCXNetwork | Keep custom base network descriptor. `BTCPayNetwork`/overload wires Bitcoin/NBXplorer assumptions and is not safe for 286-byte PoCX blocks without a PoCX-aware backend |
| Payment handler | `BTCPayServer/Payments/IPaymentMethodHandler.cs` | `IPaymentMethodHandler`; `PaymentMethodContext` | `Payments/Bitcoin/BitcoinLikePaymentHandler.cs` | BTCX handler owns prompt snapshot, destination/script tracking token and parsing. Bitcoin handler is behavioral reference only, not reusable as-is |
| Payment destination tracking | `BTCPayServer/Payments/Bitcoin/BitcoinLikePaymentHandler.cs`; `BTCPayServer/Services/Invoices/InvoiceRepository.cs` | `PaymentMethodContext.TrackedDestinations`; `InvoiceRepository.GetInvoiceFromAddress` | Bitcoin-like handler adds script hash | Persist a deterministic script-derived token with invoice at prompt configuration; listener resolves tracked invoices and independently verifies output script |
| Prompt/link | `BTCPayServer/Payments/IPaymentLinkExtension.cs`; `Payments/Bitcoin/BitcoinPaymentLinkExtension.cs` | `IPaymentLinkExtension.GetPaymentLink(PaymentPrompt, IUrlHelper?)` | Bitcoin link extension uses BIP21 | BTCX extension emits Phoenix-proven `btcx:` URI and fixed decimal amount |
| Checkout | `BTCPayServer/Payments/ICheckoutModelExtension.cs`; `Payments/Bitcoin/BitcoinCheckoutModelExtension.cs` | `ICheckoutModelExtension.ModifyCheckoutModel(CheckoutModelContext)` | Bitcoin checkout extension | Render address/QR/link and BTCX snapshot; no claim of payment until listener records it |
| Listener lifecycle | `BTCPayServer/Payments/Bitcoin/NBXplorerListener.cs`; service registration `BTCPayServer/Hosting/BTCPayServerServices.cs` | `IHostedService`, `EventAggregator` | `NBXplorerListener` | BTCPay exposes no generic payment-listener interface. Implement plugin-owned hosted service + polling/reconciliation; events are wakeups, persisted invoice/script state is recovery source |
| Payment persistence | `BTCPayServer/Services/Invoices/PaymentService.cs`; `BTCPayServer/Data/PaymentData.cs` | `PaymentService.AddPayment`, `UpdatePayments`; `PaymentData`/`PaymentEntity` | NBXplorer listener | Add per-output payments idempotently, update confirmations/status; publish receipt/update events as needed |
| Invoice state aggregation | `BTCPayServer/HostedServices/InvoiceWatcher.cs`; invoice event definitions under `BTCPayServer/Services/Invoices/` | `InvoiceEvent.ReceivedPayment`, invoice update event | InvoiceWatcher | Let core aggregate payment totals, expiry and invoice status; plugin owns only validated BTCX observations and confirmation status |

Pinned source links:

- [IPaymentMethodHandler.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Payments/IPaymentMethodHandler.cs)
- [BitcoinLikePaymentHandler.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Payments/Bitcoin/BitcoinLikePaymentHandler.cs)
- [NBXplorerListener.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Payments/Bitcoin/NBXplorerListener.cs)
- [PaymentService.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Services/Invoices/PaymentService.cs)
- [InvoiceWatcher.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/HostedServices/InvoiceWatcher.cs)
- [InvoiceRepository.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer/Services/Invoices/InvoiceRepository.cs)
- [BTCPayNetwork.cs](https://github.com/btcpayserver/btcpayserver/blob/2d5a0d8077bb33af080e949031da33d84b80638d/BTCPayServer.Common/BTCPayNetwork.cs)

## BTCX network and encoding facts

The BTCX integration root is [bitcoin-pocx at `005bf009`](https://github.com/PoC-Consortium/bitcoin-pocx/tree/005bf0098e217b76a2627bfae458dff4f5718dd5); its Bitcoin implementation is a submodule pinned at [`b88b852`](https://github.com/PoC-Consortium/bitcoin/tree/b88b852644f629cd5f25b3424d11b462462c24b3). Chain parameters are in `src/kernel/chainparams.cpp`; default RPC ports in `src/chainparamsbase.cpp`; PoCX header layout in `src/primitives/block.h` and hash behavior in `src/primitives/block.cpp`.

| Network | P2P magic | P2P port | RPC port | P2PKH/P2SH prefix | Bech32 HRP |
|---|---|---:|---:|---|---|
| Mainnet | `a7 3c 91 5e` | 8338 | 8332 | `0x55` / `0x5a` | `pocx` |
| Testnet | `6d f2 48 b4` | 18338 | 18332 | `0x7f` / `0x84` | `tpocx` |
| Regtest | `fa bf b5 da` | 18444 | 18443 | `0x6f` / `0xc4` | `rpocx` |

All use 8 decimals, `100,000,000` atomic units/BTCX, and max money `21,000,000 * 100,000,000` atomic units. Target block interval is 120 seconds. Dust is node relay policy dependent (`dustrelayfee` and script type), not a universal consensus value; validate with the pinned node's mempool policy. BTCX regtest and Bitcoin regtest share default P2P port/magic but have different genesis; use explicit isolated ports when both run.

PoCX `CBlockHeader` serializes to 286 bytes: common 80-byte fields plus height, generation signature, base target, proof fields, public key and signature. Under PoCX, `GetHash` zeroes the trailing signature before hashing. Normal transactions remain Bitcoin-style, but block/header parsing and tx offset discovery must understand this format.

## Address → script → transaction output

Pinned Rust BTCX implementation: [PoC-Consortium/btcx at `6907bacb`](https://github.com/PoC-Consortium/btcx/tree/6907bacb132324e460cbe55d3765b4350bb56e61), especially `params-btcx/src/params.rs`, `registry.rs`, `keys-btcx/src/lib.rs`, and `wallet-btcx`.

1. Select the explicit BTCX network's address parameters (HRP and Base58 version bytes).
2. Decode/check checksum and network. The Rust `ChainParams.parse_address` maps witness v0/v1 with matching HRP into witness program script; Base58 payload must be 21 bytes and maps P2PKH to `OP_DUP OP_HASH160 <hash160> OP_EQUALVERIFY OP_CHECKSIG`, P2SH to `OP_HASH160 <hash160> OP_EQUAL`.
3. Store the resulting script bytes and a stable script-hash/tracking token with the invoice.
4. A transaction output is `(integer value in atomic units, scriptPubKey bytes)`. Match exact script bytes, validate positive/range/dust policy, then convert atomic units to display BTCX.

Do not compare addresses as strings to detect payments. Wallet crate supports BIP84 `wpkh` and BIP86 `tr`; main coin type is `0x504F4358`, test/regtest uses SLIP-44 test coin type `1'`. Derivation path/account and Phoenix-compatible output type must be explicitly aligned; do not infer from the address prefix.

## Discovery backends and wallet choices

| Option | Audit result | Advantages | Risks / use |
|---|---|---|---|
| PoCX RPC polling | Node is canonical authority; generic block/mempool/raw tx methods are available, but address history needs wallet tracking or scanning/indexing | Few dependencies, canonical chain and reorg truth | Scanning whole chain per invoice is unsuitable; RPC auth/network exposure and recovery logic required. Use for verification/reconciliation, not naive repeated full-chain scans |
| `electrs-btcx` + `bindex-btcx` | electrs serves Electrum protocol; bindex parses PoCX 286-byte blocks and uses node REST endpoints | Script-hash history/subscriptions/mempool discovery; bindex README reports mainnet validation | Extra service/index operations; pin node/backend and independently regtest-test. Preferred discovery candidate, never sole settlement authority |
| `bindex-btcx` alone | Library, not a complete payment API/service | Reusable PoCX-aware indexing logic | Needs a service/indexer wrapper; not directly a listener |
| `esplora-pocx` | Audited repository is primarily frontend/API documentation; backend is separate `esplora-electrs-pocx` | Familiar Esplora REST model | Backend revision not pinned/audited here. Do not assume frontend repo is a production indexer |

Pinned audit commits: [electrs-btcx `2f78c63`](https://github.com/PoC-Consortium/electrs-btcx/tree/2f78c63e20215e20944767f0901209c4d740fe5b), [bindex-btcx `eda7c70`](https://github.com/PoC-Consortium/bindex-btcx/tree/eda7c70660baa06affef464c7ea1e131c39304f1), [esplora-pocx `2b7e1c8`](https://github.com/PoC-Consortium/esplora-pocx/tree/2b7e1c8a5d2dde2d688974e5bdaf604d223283c8). Recommendation: electrs/bindex for discovery plus PoCX node RPC for canonical transaction/block/confirmation reconciliation, contingent on isolated regtest verification.

Wallet options: node wallet RPC is fastest for first isolated receiver but keeps hot private keys at the node; isolate it, back it up, and expose no withdrawal path. A watch-only descriptor/xpub allocator minimizes plugin key exposure and is preferred for production receiving if address index durability/recovery is proven. The Rust BTCX wallet is a capable BDK/SQLite wallet with seed/xprv-oriented functionality; embedding it in .NET adds FFI/service and secret lifecycle complexity. A separate wallet/address service creates a clean trust boundary but is an additional operational component. Stage recommendation: dedicated regtest node-wallet RPC prototype; production decision gate favors isolated watch-only address allocation/service. Receiving only; withdrawals are out of scope.

## Payment data and state ownership

In BTCPay v2.4.4 `PaymentStatus` values are `Processing`, `Settled`, `Unaccounted`; there is no generic `Confirmed` status. Listener records valid mempool/on-chain output as `Processing`, promotes to `Settled` only after configured BTCX confirmation threshold and canonical-chain verification, and changes to `Unaccounted` when a replaced/disappeared output no longer counts. `PaymentService` persists and emits settlement transitions; `InvoiceWatcher` aggregates payments, nominal due, expiry/invalid and final invoice status. Do not mark a payment settled merely because a transaction appeared in mempool.
