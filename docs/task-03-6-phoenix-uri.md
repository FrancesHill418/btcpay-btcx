# TASK 03.6 — Phoenix PoCX BTCX Payment URI

Status: implemented from pinned Phoenix PoCX v2.4.0 source. BTCPay displays the generated URI and QR through the standard BTCPay v2.4.4 on-chain checkout component. URI vectors are unit tested; no wallet transaction has been sent.

## Phoenix source findings

Pinned Phoenix PoCX v2.4.0 commit `bc4713306c9c2cd3cbf989a3e355e0705b485218`:

- `web-wallet/src/app/bitcoin/utils/payment-uri.ts` declares outbound canonical scheme `btcx` and emits `<scheme>:<address>?amount=<value>`.
- `parsePaymentUri` accepts `btcx`, `pocx`, `bitcoin`, and bare addresses. It strips the scheme and query, reads `amount` with `URLSearchParams`, converts it to a positive finite numeric amount, then validates the extracted address against the wallet's PoCX address/network validator.
- `handbook/chapters/ch07-receiving.md` documents the `btcx:` URI, amount prefill, and 8 decimal places (1 BTCX = 100,000,000 atomic units).
- URI contains no explicit network field; network validation comes from address HRP/checksum against the active wallet network. `bitcoin:` is an inbound compatibility scheme for the upstream Qt wallet, not the canonical outgoing scheme.

## Plugin behavior

The plugin emits exactly `btcx:<lowercase-BTCX-address>?amount=<fixed-decimal>`. Amount is created from immutable invoice atomic units and formatted invariantly with no exponent and no unnecessary trailing zeros. The builder validates saved address, network and script against the invoice snapshot and verifies decimal and atomic amounts agree. Invalid or inconsistent prompt details produce no payment link.

BTCPay v2.4.4's `BitcoinCheckoutModelExtension.CheckoutBodyComponentName` is reused without changing BTCPay core. The plugin sets standard `InvoiceBitcoinUrl`, `InvoiceBitcoinUrlQR`, and the existing prompt destination. The shipped Bitcoin-like checkout view renders the address, wallet link, copy action, and QR value. QR payload is byte-for-byte the same Phoenix-compatible URI.

## Verification

Tests cover 37.5, 0.1, 0.00000001, and 1 BTCX, checking the exact URI string, exact address extraction, and amount round trip. They also reject wrong-network addresses, mismatched scripts, and malformed addresses. These vectors follow the pinned Phoenix parser behavior (`URLSearchParams` + positive finite amount) and source's address validation contract.

No Phoenix GUI/parser executable or isolated regtest environment was run here, so this is source-verified URI compatibility rather than a live Phoenix wallet integration. Regtest QR scan and send/receive remains part of final development acceptance. Mainnet remains disabled by the receiving wallet configuration.
