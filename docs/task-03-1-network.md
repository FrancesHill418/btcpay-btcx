# TASK 03.1 — BTCX network, address/script and amount support

Status: implemented in the plugin. This adds static network/address/amount primitives only. It does not add RPC, wallet, transaction monitoring, confirmation, reorg, withdrawal or payment settlement behavior.

## Locked source revisions

- Bitcoin-PoCX integration repository: [`005bf0098e217b76a2627bfae458dff4f5718dd5`](https://github.com/PoC-Consortium/bitcoin-pocx/tree/005bf0098e217b76a2627bfae458dff4f5718dd5). Its `bitcoin` source submodule is [`b88b852644f629cd5f25b3424d11b462462c24b3`](https://github.com/PoC-Consortium/bitcoin/tree/b88b852644f629cd5f25b3424d11b462462c24b3); the relevant paths in that pinned tree are `bitcoin/src/kernel/chainparams.cpp`, `bitcoin/src/chainparamsbase.cpp`, `bitcoin/src/primitives/block.h`, `bitcoin/src/primitives/block.cpp`, `bitcoin/src/consensus/amount.h`, `bitcoin/src/policy/policy.h`, and `bitcoin/src/policy/policy.cpp`.
- BTCX Rust wallet/parameter crate: [`6907bacb132324e460cbe55d3765b4350bb56e61`](https://github.com/PoC-Consortium/btcx/tree/6907bacb132324e460cbe55d3765b4350bb56e61), especially `params-btcx/src/params.rs`, `params-btcx/src/registry.rs`, `keys-btcx/src/lib.rs`, and `wallet-btcx`.
- Phoenix PoCX: release `v2.4.0`, commit [`bc4713306c9c2cd3cbf989a3e355e0705b485218`](https://github.com/PoC-Consortium/phoenix-pocx/tree/bc4713306c9c2cd3cbf989a3e355e0705b485218). The task message had `be471...`; the checkout and project lock both identify `bc471...`.
- BTCPay Server: `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d`.

The source repositories were checked out at these commits to read the implementation directly. The Bitcoin-PoCX checkout contains the Bitcoin implementation as a git submodule; it does not put these source files at repository-root `src/...`.

## Network parameters

The C++ chain parameters (`ENABLE_POCX` branches), RPC base parameters and Rust `BTCX_*` constants agree on the following values:

| BTCX network | Chain identifier | P2P magic | P2P port | RPC port | P2PKH | P2SH | Witness HRP | Genesis hash |
|---|---|---|---:|---:|---:|---:|---|---|
| Mainnet | `main` | `a73c915e` | 8338 | 8332 | `0x55` | `0x5a` | `pocx` | `6ab422073e327d42a0e5dfaaa26564324ddb225e53c64da89283cd4e3dfb7ac6` |
| Testnet | `test` | `6df248b4` | 18338 | 18332 | `0x7f` | `0x84` | `tpocx` | `181c51a172fe20c203e463f6f203b7d9be388fa0f1282e507192f94d24a57e81` |
| Regtest | `regtest` | `fabfb5da` | 18444 | 18443 | `0x6f` | `0xc4` | `rpocx` | `2a98a52253aeff06093948b00568d380b7634621bc606403127973c9acbbfde0` |

All BTCX networks target 120-second blocks. Under `ENABLE_POCX`, headers serialize to 286 bytes; PoCX's `CBlockHeader::GetHash` zeroes the trailing 65-byte signature before hashing. This is recorded as a network/source fact only; this task does not parse blocks. BTCX regtest intentionally shares Bitcoin regtest's magic and default ports, so local simultaneous nodes need explicit distinct ports.

The BTCX Rust registry declares 8 decimals. `consensus/amount.h` sets `COIN = 100,000,000` and `MAX_MONEY = 21,000,000 * COIN` (2,100,000,000,000,000 atomic units). Zero is representable as a value, but the minimum positive amount is one atomic unit (0.00000001 BTCX).

Dust is relay policy, not an amount or consensus minimum. Pinned Core defines `DUST_RELAY_TX_FEE = 3000 sat/kvB` by default and calculates an output-specific threshold from the output serialization and expected spend size; at that default the source comments give 546 sat for typical legacy and 294 sat for P2WPKH outputs. Runtime `-dustrelayfee` can change it. The plugin therefore does not reject a BTCX amount based on a hard-coded dust threshold.

## Address and script formats

`BtcxAddress.Parse` and `FromScriptPubKey` implement the same address classes as the pinned Rust parser and Phoenix validator:

- Base58Check P2PKH and P2SH use network-specific version bytes and a 20-byte HASH160.
- Witness v0 uses Bech32 and requires a 20-byte or 32-byte witness program.
- Witness v1 uses Bech32m and a 2–40-byte witness program.
- Higher witness versions are rejected: while the general Rust helper can construct them, Phoenix v2.4.0's validator explicitly supports only v0 and v1. This keeps generated/parser output within the wallet behavior confirmed by source.
- Script conversion is exact: P2PKH, P2SH, and the two supported witness script forms round-trip address → scriptPubKey → address. Other scriptPubKey forms raise `NotSupportedException`.

A well-formed BTCX witness address with another BTCX HRP is rejected as a network mismatch. Unlike Bitcoin's shared test/regtest address versions, BTCX testnet and regtest use distinct Base58 prefixes (`0x7f`/`0x84` versus `0x6f`/`0xc4`), so legacy addresses distinguish these networks too.

## Fixture provenance

Tests use static Phoenix PoCX v2.4.0 test fixtures `pocx1qcpueamxr0aa82t7dtvhzdksq59c993f9heu9te` and `pocx1qc7axsl082uqm0t3fuqefy7rw2ug52pl338esjk` from `web-wallet/src/app/bitcoin/services/wallet/derivation-roundtrip.spec.ts`. Expected witness script bytes were decoded independently from those values. The `tpocx` and `rpocx` variants use the same fixture witness program and checksums recomputed under the respective source-defined HRPs.

The Base58 BTCX vectors are derived from the pinned Rust `params.rs` Base58 tests' HASH160 fixtures (P2PKH `77bff2...e839a` and `243f13...ce924`; P2SH `b472a2...b9fcb` and `4e9f39...305b0`) encoded with the Bitcoin-PoCX source's BTCX prefix bytes, including the differing testnet and regtest versions. Expected scriptPubKey bytes are explicit in the test cases. These are deterministic compatibility fixtures, not private-key or mainnet-payment fixtures.

## Amount representation

`BtcxAmount` stores a signed 64-bit integer count of atomic units and enforces `0..MAX_MONEY`. Its decimal/text constructors reject values above 8 decimal places, negative values, overflow and exponent notation. Text output is invariant fixed-point, optionally with trailing zeros trimmed. No floating-point value is used for amount conversion. Existing manual-rate conversion now validates the rounded result against this BTCX amount range before returning it to the payment prompt.

Examples: `0` → `0` atomic units; `0.00000001` → `1`; `1` → `100000000`; `37.5` → `3750000000`; maximum `21000000` → `2100000000000000`.

## BTCPay integration

BTCPay v2.4.4 exposes `BTCPayNetworkBase` for currency metadata such as crypto code and divisibility, plus `CurrencyData` registration. The existing `BTCXNetwork` remains a `BTCPayNetworkBase` descriptor and now exposes the explicit BTCX network definitions; plugin divisibility comes from `BtcxAmount.Decimals`.

The pinned BTCPay Bitcoin address APIs require an NBitcoin `Network` and would apply Bitcoin/NBXplorer assumptions. There is no PoCX-aware `BTCPayNetwork` or address implementation in v2.4.4, so these changes do not register BTCX as Bitcoin's `BTCPayNetwork`, create a fake NBitcoin network, or touch BTCPay core. `BtcxAddress` parses and serializes custom BTCX addresses to NBitcoin `Script` objects independently. Payment handler/address allocation remains out of scope.

## Phoenix compatibility

Phoenix's pinned `address-validation.ts` lists exactly these HRPs/prefixes and decodes Bech32/Bech32m for witness versions 0/1. Its derivation round-trip tests provide the mainnet addresses used above. Therefore the address formats emitted by `BtcxAddress.FromScriptPubKey` for P2PKH, P2SH, witness v0 and v1 are structurally within Phoenix's source-validated formats. The canonical future payment URI is `btcx:<address>?amount=<fixed decimal>`; Phoenix's `payment-uri.ts` emits scheme `btcx`, accepts amount in whole BTCX, and its amount model uses 10^8 atomic units. This task does not create a payment URI, generate wallet addresses, scan a QR, or exercise Phoenix at runtime.

## Tests

`BtcxNetworkAddressAmountTests` covers all three network parameter sets, mainnet/testnet/regtest witness fixtures, Base58 P2PKH/P2SH script vectors, network mismatch, checksum/malformed/unsupported types, exact address/script round trips, amount zero/minimum/1 BTCX/37.5 BTCX/maximum, and precision/range rejection.
