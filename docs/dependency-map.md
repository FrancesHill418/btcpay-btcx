# BTCX dependency map

**Scope:** source-level architectural audit only. No service was installed or exercised. Repository revisions are not pinned because the local project checkout is absent.

| System | What current upstream says it does | Useful for | Not established / caution |
|---|---|---|---|
| `bitcoin-pocx` | Bitcoin Core-derived node with PoCX consensus, RPC, wallet and canonical chain/UTXO state; current docs describe 286-byte headers and signature-zeroed block hashes | Authoritative chain validation, block/transaction RPC, regtest/testnet node, possible wallet backend | BTCX-specific RPC methods/options and compatibility must be checked against the chosen tag. Don't assume vanilla Bitcoin transaction/header behavior. |
| `btcx` | Rust workspace: `params-btcx`, `keys-btcx`, `seedstore`, `electrum-btcx`, `wallet-btcx` (BDK v2). Supports BTCX chain params, address parsing, key derivation, encrypted seed-at-rest and Electrum synchronization | Reference for BTCX network/address/key/wallet semantics; possible operational wallet stack | Not a BTCPay plugin or node RPC service; Rust libraries need an explicit, reviewed integration boundary before reuse from .NET. Never copy seed-store/key code into the BTCPay invoice path casually. |
| `bindex-btcx` | BTCX fork of Rust `bindex`; indexes BTCX headers; reads from Bitcoin-PoCX Core REST; requires Core v31+ and `-rest` | Indexing blocks and transaction data for Electrum server | It is a library/indexing component rather than a documented merchant-facing payment API. REST is unauthenticated on node RPC port per electrs docs; keep private. |
| `electrs-btcx` | BTCX Electrum protocol server based on romanz/electrs and `bindex-btcx`; supports history/scripthash subscription flows for nodeless wallets | Script history, transaction lookup, mempool notifications via Electrum protocol; wallet synchronization | It serves Electrum, not Esplora HTTP; repository describes wallet/swap consumers, not a BTCPay integration contract. Need persistence/reorg/restart behavior proven for selected revision. |
| `esplora-pocx` | Separate fork based on Blockstream Esplora/electrs, with REST API including address/scripthash history, UTXO, transaction status/raw tx, block and tip endpoints | Convenient HTTP lookup for scripts/transactions/UTXOs and a possible plugin adapter | API shape alone does not establish finality, reorg guarantees, authentication, pagination completeness, mempool consistency or production SLA. Must self-host and reconcile canonical node data. |
| BTCPay `NBXplorer` | BTCPay's Bitcoin-family wallet/indexing integration | Potentially useful only if BTCX satisfies all protocol and consensus assumptions | BTCX has nonstandard block-header length/hash behavior. No compatibility was established in this audit; do not point NBXplorer at BTCX or label BTCX as Bitcoin without proof. |

## Data responsibility

| Data/action | Primary authority | Candidate query surface | Payment-plugin rule |
|---|---|---|---|
| Consensus, canonical block, chainwork/tip | `bitcoin-pocx` node | Node RPC | Treat the node as authoritative for canonical chain and confirmations. |
| UTXO set and spend validity | Node chainstate | Node RPC; indexer UTXO endpoints for fast lookup | Indexer results are hints; reconcile against canonical node and outpoint spend status. |
| Address/script history | Indexer | `esplora-pocx` REST or `electrs-btcx` Electrum scripthash history | Subscribe/poll using scriptPubKey identity; address text is not the stable chain key. |
| Raw transaction / txid / outputs | Node | Node RPC; indexer REST for retrieval | Parse outputs with BTCX-aware serialization rules; compare exact script and integer base units. |
| Mempool observation | Node mempool or indexer notifier | Node notification/RPC; Electrum subscription/Esplora mempool endpoint | Mempool payment is provisional; dedupe and re-check after restart/replacement/double-spend. |
| Confirmation count | Node best-chain height + tx's canonical block inclusion | RPC plus indexer tx status | `tipHeight - inclusionHeight + 1` only while inclusion block remains in best chain. Reverse on reorg. |
| Receive address/script | BTCX-compatible descriptor/key derivation or node wallet | `btcx` params/keys/wallet; node wallet RPC as separately evaluated option | Derive fresh invoice receive destination; testnet/regtest only during development. |
| Spend/sign/broadcast | Wallet signer + node broadcast | `wallet-btcx` or node wallet RPC | Separate subsystem and permissions from receive-only payment detection. Withdrawals must not block accepting payments. |

## Indexer choice for first payment listener

Two reasonable first adapters are:

| Choice | Complexity | Security | Maintenance | BTCPay compatibility | BTCX compatibility | XBoard compatibility |
|---|---|---|---|---|---|---|
| Esplora REST + canonical node reconciliation | Low-to-medium: ordinary HTTP client, but implement paging/polling/recovery | Good when self-hosted/private; API may expose sensitive address histories; node remains private | REST is easy to mock, but fork-specific behavior and deployment must be maintained | Neutral: plugin can call it without changing BTCPay core | BTCX fork provides BTCX-specific API; test header/tx semantics | Neutral; XBoard never sees indexer |
| Electrum protocol + `electrs-btcx` | Medium: protocol subscriptions, reconnect and script-hash handling | Good when bound privately; dedicated indexed service; separate underlying REST exposure remains | More protocol and service lifecycle complexity | Neutral | Directly targets BTCX, with BDK wallet ecosystem | Neutral; XBoard never sees indexer |

**Recommendation:** start with a small Esplora HTTP adapter if and only if the selected `esplora-pocx` revision exposes reliable per-script history/status and the local integration test proves mempool/reorg/restart recovery. Otherwise implement Electrum subscriptions with a durable cursor. In either case use node RPC as canonical reconciliation. Do not make indexer-only final-payment decisions. This is a provisional engineering choice, not a validated deployment choice; the local checkout and indexer implementations still need review.

## Sources

- [bitcoin-pocx](https://github.com/PoC-Consortium/bitcoin-pocx)
- [BTCX wallet stack](https://github.com/PoC-Consortium/btcx)
- [bindex-btcx](https://github.com/PoC-Consortium/bindex-btcx)
- [electrs-btcx BTCX notes](https://github.com/PoC-Consortium/electrs-btcx/blob/btcx/README-BTCX.md)
- [esplora-pocx REST API](https://github.com/PoC-Consortium/esplora-pocx/blob/pocx/API.md)
