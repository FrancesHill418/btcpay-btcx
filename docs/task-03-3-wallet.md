# TASK 03.3 — BTCX Receiving Wallet / Address Provider

Status: implemented for development/regtest use. No node or wallet was contacted by unit tests.

## Decision and source audit

Use a dedicated, isolated bitcoin-pocx node wallet and its wallet-scoped RPC. This is the smallest implementation supported by the pinned node and avoids importing/handling seed material in the plugin. The repository's node source is pinned to `005bf0098e217b76a2627bfae458dff4f5718dd5`; its Bitcoin wallet submodule is `b88b852644f629cd5f25b3424d11b462462c24b3`.

Audited implementation:

- `bitcoin/src/wallet/rpc/addresses.cpp`: `getnewaddress(label, address_type)` returns an encoded address and adds it to the wallet address book with receive purpose. Supported output types are enumerated by `FormatAllOutputTypes`; this integration explicitly requests `bech32` (native witness v0).
- The same file implements `getaddressesbylabel(label)`, returning `{ address: { purpose } }`. No match returns `RPC_WALLET_INVALID_LABEL_NAME` (`-11`, `bitcoin/src/rpc/protocol.h`).
- Wallet RPC handlers are registered under the wallet category (`bitcoin/src/wallet/rpc/wallet.cpp`); calls use `/wallet/{name}`. The address encoder uses the selected BTCX chain parameters, so the generated HRP follows the active PoCX network.
- `getblockchaininfo` reports the chain ID; `getblockhash(0)` returns the active genesis hash. Both are checked before allocation to reject a wrong network or an unexpected chain with a reused name.

The pinned BTCX Rust wallet supports BIP84/BIP86 and has wallet/seed lifecycle features, but integrating its Rust/BDK/SQLite secret lifecycle into this .NET plugin would add an FFI or separate service without improving this development milestone. The dedicated node wallet is therefore used for the receiving prototype. No Rust wallet source or keys are embedded.

## Allocation and recovery

`IBtcxReceiveAddressProvider.GetOrAllocateAsync(invoiceId)` derives a stable, non-identifying wallet label from SHA-256(invoice ID), checks that label first, and calls `getnewaddress` only if no receive address exists. A process-wide lock serializes allocation requests within one BTCPay process. The result is parsed by the BTCX address implementation, required to be witness v0 and for the configured network, then returned with its scriptPubKey and `btcx-script:<sha256(scriptPubKey)>` tracking token.

The payment prompt persists destination and snapshot (`network`, address, scriptPubKey hex, BTCX amount in atomic units) through BTCPay's standard invoice blob. BTCPay also persists the tracked destination in `AddressInvoices`. Thus normal process restart recovery reads the saved prompt; if address generation succeeded but invoice persistence did not, the stable wallet label recovers the same address on retry. `getnewaddress` is not automatically retried after ambiguous transport failure because repeating a state-changing RPC could consume another address. A later activation retry first searches the stable label.

Only receive address, script, network and amount are stored in the invoice. No private key, seed, cookie or RPC password is stored in prompt details or logs. Addresses with malformed checksums, unsupported type or wrong network fail closed before being returned.

## BTCPay integration

- `BtcxPaymentMethodHandler.ConfigurePrompt` validates the current manual quote, converts the quote to integer atomic units, allocates a destination, assigns `Prompt.Destination`, and records the script-hash tracking token in `PaymentMethodContext.TrackedDestinations`.
- `InvoiceRepository` in the pinned BTCPay v2.4.4 source persists those destinations in its `AddressInvoices` table when it saves/activates a prompt. No BTCPay core changes are needed.
- RPC/wallet services are lazy: BTCPay host startup and unrelated payment methods do not require RPC credentials or a running BTCX wallet. When BTCX is selected, unavailable/invalid wallet configuration prevents the BTCX prompt from being created.

## Security boundary and limitations

Use only a dedicated development/regtest wallet for this implementation. The node wallet's cookie/RPC authentication is not a read-only capability: anyone holding it can invoke other wallet RPC methods. The plugin exposes only the narrow receiving operations in its address provider and has no withdrawal feature, but that does not reduce the authority of a compromised node cookie. Keep the node and wallet on an isolated internal network, restrict filesystem access to its cookie and wallet directory, and keep RPC off public interfaces. Take and test encrypted/offline wallet backups with the node's supported wallet backup tooling before any value-bearing use.

Production receiving should use a separately reviewed watch-only descriptor/xpub allocator or a dedicated address service with durable derivation-index reservation and restore procedures. Do not point this development wallet configuration at mainnet or a production wallet. No wallet was started or connected in this task.

## Tests

Mock JSON-RPC tests cover distinct invoice labels/addresses, stable-label recovery after provider recreation, checksum rejection, a valid wrong-network address, wrong-chain rejection before address generation, and genesis-hash verification. The broader RPC tests continue to check private endpoints, authentication, timeout, retry, malformed response and cancellation. Tests use generated public witness scripts; no private keys are present.
