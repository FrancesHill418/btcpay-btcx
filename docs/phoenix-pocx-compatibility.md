# TASK 02.2 — Phoenix PoCX compatibility audit

Audit date: 2026-09-28  
Audited Phoenix release: `v2.4.0`  
Commit: [`bc4713306c9c2cd3cbf989a3e355e0705b485218`](https://github.com/PoC-Consortium/phoenix-pocx/commit/bc4713306c9c2cd3cbf989a3e355e0705b485218)

This is a source audit and test design. No Phoenix runtime payment, live wallet, or end-to-end address transfer was run. No BTCPay core, XBoard, or business code was changed.

## Decision

When a BTCX destination is available, the BTCPay prompt should provide the address, whole-BTCX amount, a canonical `btcx:` payment URI, and a QR encoding that URI. Use a fixed decimal amount with at most eight fractional digits (for example `0.00000001`), never scientific notation. Phoenix v2.4.0 accepts `btcx:`, `pocx:`, legacy `bitcoin:`, and a bare address as input, but its canonical output scheme is `btcx:`. A `bitcoin:` URI is not a reason to accept a Bitcoin address: Phoenix validates the decoded destination as a PoCX address.

Protocol-level compatibility is supported by source. The current BTCX plugin does not yet generate an address, URI, or QR: [`BtcxPaymentLinkExtension`](../src/BTCPayServer.Plugins.BTCX/Payments/BtcxPaymentLinkExtension.cs) returns `null` pending wallet/node integration. Consequently there is no BTCPay-generated address to pass through Phoenix, and end-to-end payment compatibility remains unproven.

## Pinned Phoenix source and flow

The audited repository is [PoC-Consortium/phoenix-pocx](https://github.com/PoC-Consortium/phoenix-pocx), release `v2.4.0`, commit above. The release is listed on the [official releases page](https://github.com/PoC-Consortium/phoenix-pocx/releases). The wallet is Phoenix PoCX, not the unrelated Bitcoin Lightning Phoenix wallet.

Relevant files in that commit:

| Behavior | Source |
|---|---|
| URI schemes, parser, builder | [`payment-uri.ts`](https://github.com/PoC-Consortium/phoenix-pocx/blob/bc4713306c9c2cd3cbf989a3e355e0705b485218/web-wallet/src/app/bitcoin/utils/payment-uri.ts) |
| Address/network validation | [`address-validation.ts`](https://github.com/PoC-Consortium/phoenix-pocx/blob/bc4713306c9c2cd3cbf989a3e355e0705b485218/web-wallet/src/app/bitcoin/utils/address-validation.ts) |
| Receive address, URI and QR | [`receive.component.ts`](https://github.com/PoC-Consortium/phoenix-pocx/blob/bc4713306c9c2cd3cbf989a3e355e0705b485218/web-wallet/src/app/features/receive/pages/receive/receive.component.ts) |
| Send input and dispatch | [`send.component.ts`](https://github.com/PoC-Consortium/phoenix-pocx/blob/bc4713306c9c2cd3cbf989a3e355e0705b485218/web-wallet/src/app/features/send/pages/send/send.component.ts) |
| BTCX display and atomic conversion | [`amount.pipe.ts`](https://github.com/PoC-Consortium/phoenix-pocx/blob/bc4713306c9c2cd3cbf989a3e355e0705b485218/web-wallet/src/app/shared/pipes/amount.pipe.ts) |
| Wallet address/send methods | [`wallet.service.ts`](https://github.com/PoC-Consortium/phoenix-pocx/blob/bc4713306c9c2cd3cbf989a3e355e0705b485218/web-wallet/src/app/bitcoin/services/wallet/wallet.service.ts) |

Receive obtains a new Bech32 address from the wallet service, optionally adds amount/label, builds the payment URI, displays/copies both address and URI, and binds the URI to the QR component. The amount entry is optional, has `step=0.00000001`, and has a zero minimum. Send accepts a URI prefilled through its `uri` query parameter or typed/pasted in the destination field; it parses URI data, validates the address, checks it against the active network, and dispatches the amount to the wallet send method.

No camera QR scanning flow was found in the audited send UI. Thus the QR proof to design is: decode the displayed QR to URI text, then feed that URI to the same send/deep-link parser. This audit does not claim Phoenix scans its own QR with a camera.

## Address and network compatibility

The validator maps PoCX address prefixes as follows:

| Network | Bech32 HRP | P2PKH version | P2SH version |
|---|---:|---:|---:|
| Mainnet | `pocx` | `0x55` | `0x5a` |
| Testnet | `tpocx` | `0x7f` | `0x84` |
| Regtest | `rpocx` | `0x6f` | `0xc4` |

It recognizes PoCX Bech32/Bech32m and Base58Check addresses and maps them to a network. The send page separately rejects a syntactically valid address when its network differs from the active wallet network. BTC Bitcoin prefixes such as `bc1`/`tb1` are not the PoCX HRPs; Bitcoin Base58 versions are not the PoCX versions. The URI scheme alone does not make an address valid.

The existing address-validation spec covers representative mainnet/testnet legacy and witness addresses, checksum corruption, and garbage. It does not establish every malformed witness-program length or all regtest cross-network UI paths; those are explicit test cases below. Source review alone is not a substitute for a generated address from the pinned PoCX node/wallet.

## URI and amount semantics

`payment-uri.ts` defines accepted input schemes as `btcx`, `pocx`, and `bitcoin`, and canonical output as `btcx`. Parsing extracts `amount` through JavaScript `Number`; it retains only finite values greater than zero. It does not enforce a maximum money value or eight-decimal precision. The builder serializes the provided number using `String(amount)`.

Phoenix amounts are whole BTCX, not satoshis. `amount.pipe.ts` uses `100,000,000` atomic units per BTCX and rounds BTCX to atomic units with `Math.round(amount * 100000000)`. The display ticker is BTCX and display precision is up to eight decimal places. Therefore expected conversions are:

| BTCX | Atomic units (satoshi-like) | Expected displayed amount |
|---:|---:|---:|
| `37.5` | `3,750,000,000` | `37.5 BTCX` |
| `0.1` | `10,000,000` | `0.1 BTCX` |
| `0.00000001` | `1` | `0.00000001 BTCX` |

Use decimal arithmetic and the BTCX divisibility of 8 on the BTCPay side. Never pass atomic units as the URI `amount`, and never convert a whole-BTCX value to atomic units twice. The intended string for the smallest unit is `amount=0.00000001`.

### Scientific-notation interoperability finding

Because JavaScript `String(0.00000001)` returns `1e-8`, Phoenix's own URI builder can emit `btcx:<address>?amount=1e-8` for one atomic unit. Its own parser accepts that numeric spelling. Generic BIP21 consumers may not accept exponent notation. BTCPay should therefore format URI amounts using invariant fixed-point decimal notation with no exponent and no unnecessary trailing zeroes. This also gives a deterministic value for QR text and copied URI.

### Minimum and maximum

One atomic unit (`0.00000001 BTCX`) is the representable precision floor, not a guarantee that a transaction of that value is relayable. Practical dust thresholds depend on output script and node relay policy; Phoenix's UI does not define a universal transaction minimum and delegates transaction acceptance to the wallet/node stack. Test dust rejection against the pinned node/backend rather than hard-coding one wallet UI threshold.

The PoCX Core money range is expected to follow Bitcoin's 21 million coin bound, i.e. `2,100,000,000,000,000` atomic units (`21,000,000 BTCX`); verify this against the exact Core chain parameters used by the deployment before enforcing it in the plugin. Phoenix URI parsing itself does not impose that ceiling. The plugin should reject values above the chain's consensus maximum and values not representable at 8 decimals before presenting a payable prompt.

## Test design (not executed)

Tests below are proposed for a future compatibility/integration test task. They were not run because the plugin has no BTCX address/payment-link implementation and no configured Phoenix wallet/node in this task.

| Area | Input/setup | Expected result |
|---|---|---|
| URI canonical output | Valid mainnet address, `37.5` BTCX | `btcx:<address>?amount=37.5`; exact address and amount preserved |
| URI amount | `0.1`, `0.00000001` | Fixed decimal values; smallest unit is `0.00000001`, never `1e-8` |
| URI metadata | Label/message with reserved characters | Proper percent-encoding; parser returns original text |
| Accepted schemes | `btcx:`, `pocx:`, `bitcoin:`, bare PoCX address | Address accepted if valid for PoCX; amount parsed when supplied |
| Wrong asset address | `bitcoin:bc1…`, `bitcoin:tb1…`, Bitcoin Base58 address | Reject as non-PoCX address despite accepted legacy URI scheme |
| Valid address formats | P2PKH, P2SH, Bech32 v0 and supported Bech32m, each network | Validate and classify expected network |
| Invalid address | Bad checksum, unknown version/HRP, malformed witness encoding/program length | Reject; no send dispatch |
| Network mismatch | Active mainnet + testnet/regtest address, and converse | Show wrong-network error and block send |
| Receive QR | Generate receive URI and decode QR pixels | Decoded QR text exactly equals displayed/copied canonical URI |
| Send parsing | Paste/deep-link canonical URI | Address and amount prefill; amount equals expected BTCX whole-unit amount |
| Amount equality | `37.5`, `0.1`, `0.00000001` | UI display and submitted amount equal prompt amount; atomic values exactly as table above |
| Decimal boundary | More than 8 fractional digits | Reject or explicitly normalize before quote; never silently round into a different payable amount |
| Amount bounds | Zero, negative, NaN, infinity, overflow, chain max, one atomic unit | Invalid values rejected; max accepted only if chain range and wallet policy permit |
| Dust | Amount below/at/above backend dust threshold, script variants | Wallet/node determines relayability; UI error is surfaced and no false “paid” state is shown |
| Round trip | BTCPay URI → Phoenix parse → send model | Exact address, amount and BTCX unit survive without scale conversion |

Because v2.4.0 has no located dedicated `payment-uri` spec, add parser/builder tests when implementing the integration. Existing address tests should be extended for witness length and all network-crossing cases.

## Current BTCPay plugin state and next compatibility gate

The prior TASK 02.1 runtime smoke test proved BTCPay can register the BTCX method, create a CNY invoice, calculate its BTCX quote, and render `37.5`/`30` BTCX snapshots. It also recorded that checkout remains a non-payable skeleton. `BtcxPaymentLinkExtension.GetPaymentLink` currently returns `null`; no BTCX destination or QR is generated. The current invoice amount is a BTCX quote derived from the CNY amount and locked manual rate, but it is not yet a receivable wallet output.

The next payment-prompt integration should:

1. Obtain a network-correct PoCX destination from a separately implemented wallet integration.
2. Render the BTCX amount from the immutable invoice snapshot, with exact 8-decimal maximum precision.
3. Create `btcx:` URI using fixed-point invariant formatting and encode that URI as QR data.
4. Validate the generated address network matches the BTCPay chain/network configuration.
5. Prove the URI/QR round trip in Phoenix v2.4.0 on mainnet-like and test networks before adding any listener/settlement behavior.

This task does not implement those steps or a payment listener.


## Development acceptance update (2026-09-28)

The pinned Phoenix v2.4.0 `parsePaymentUri` function was executed from its source checkout against `btcx:rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt?amount=37.5` and the one-atomic-unit amount. It returned the exact address, amount and regtest network; a `bitcoin:bc1…` fixture was rejected as a non-PoCX address. Result: **PROTOCOL COMPATIBLE**. This is a parser fixture only. No Phoenix device/runtime, QR camera scan, or wallet send/receive E2E was run; **REAL DEVICE E2E VERIFIED: NO**.
