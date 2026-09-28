# TASK 03.2 — BTCX Node RPC client

Status: implemented and mock-tested. This task adds authenticated node RPC transport and read-only information/lookup calls. It does not connect to a live BTCX node or wallet, or add address generation, UTXO scanning, payment listening, settlement, confirmation handling, reorg, withdrawal or XBoard behavior.

## Pinned source and BTCPay integration

- Bitcoin-PoCX: `005bf0098e217b76a2627bfae458dff4f5718dd5`.
- Bitcoin source submodule: `b88b852644f629cd5f25b3424d11b462462c24b3`.
- BTCPay Server: `v2.4.4`, `2d5a0d8077bb33af080e949031da33d84b80638d`.

The RPC behavior below was checked in the pinned `bitcoin/src/rpc/blockchain.cpp`, `net.cpp`, and `rawtransaction.cpp`; wallet-specific behavior is in `bitcoin/src/wallet/rpc/transactions.cpp`. These are ordinary RPC server implementations compiled under the Bitcoin-PoCX commit. The `ENABLE_POCX` branches change chain/block data, while the node keeps the compatible JSON-RPC methods.

`Plugin.Execute(IServiceCollection)` registers `IBtcxRpcClient` as a typed `HttpClient` using BTCPay's supported plugin DI extension point. No hosted service is needed for this task: the client performs explicit calls and does not poll or retain chain state. Configuration is lazy so an unconfigured node does not stop BTCPay plugin startup.

## RPC method contract and source-confirmed semantics

| Client method | RPC method and parameters | Result / behavior |
|---|---|---|
| `GetBlockchainInfoAsync` | `getblockchaininfo`, `[]` | `chain`, `blocks`, `headers`, `bestblockhash`, plus PoCX-specific `base_target` and `generation_signature` under `ENABLE_POCX`. DTO tolerates additional fields. |
| `GetNetworkInfoAsync` | `getnetworkinfo`, `[]` | Node version/subversion/protocol, P2P connections/active state, relay-fee information and warnings. This response does not identify the selected chain. |
| `GetBestBlockHashAsync` | `getbestblockhash`, `[]` | Hash of the most-work fully validated active tip. |
| `GetBlockCountAsync` | `getblockcount`, `[]` | Height of the most-work fully validated chain; genesis is height 0. |
| `GetBlockAsync(hash, verbosity)` | `getblock`, `[hash, 1|2]` | Block JSON. Verbosity 2 includes decoded transaction objects. PoCX-specific JSON fields are retained as extension data rather than parsed as Bitcoin header fields. |
| `GetRawBlockAsync(hash)` | `getblock`, `[hash, 0]` | Serialized full block as hex. No block/header decoder is included. PoCX headers are 286 bytes and must not be parsed as 80-byte Bitcoin headers. |
| `GetRawTransactionAsync(txid, blockHash?)` | `getrawtransaction`, `[txid, 0]` or `[txid, 0, blockHash]` | Serialized transaction hex. Without a block hash this RPC searches the mempool by default; confirmed transactions require `-txindex` or an explicit block hash. |
| `GetDecodedTransactionAsync(txid, blockHash?)` | `getrawtransaction`, `[txid, 1]` or `[txid, 1, blockHash]` | Decoded transaction JSON plus hex and available chain context. Input/output details are retained as JSON elements to avoid assumptions about optional fields. |
| `GetTransactionAsync(txid, walletName)` | wallet-scoped `gettransaction`, `[txid, false, true]` at `/wallet/{walletName}` | Wallet accounting/details and an optional decoded transaction. Bitcoin-PoCX source registers this under the wallet RPC table; it is not a generic node transaction lookup. The API requires an explicit wallet route and does not use default/implicit wallet selection. |
| `GetTransactionConfirmationsAsync(txid, blockHash?)` | decoded `getrawtransaction` lookup | Returns the RPC confirmation field. A mempool transaction has no block hash and no `confirmations` field, represented here as 0 confirmations and `IsMempoolCandidate=true`. With explicit block hash, `in_active_chain=false` identifies an orphaned/stale block result. This is observation only and does not settle a payment. |
| `VerifyNetworkAsync(expected)` | `getblockchaininfo`, `[]` | Compares the reported `chain` to the locked BTCX identifiers `main`, `test`, or `regtest`. It does not infer network from `getnetworkinfo`. |

The source's `getblock` JSON adds PoCX `time_since_last_block`, `poc_time`, `base_target`, `generation_signature`, `pocx_proof`, `pubkey`, `signer_address`, and `signature` fields in the `ENABLE_POCX` build. The response DTO preserves unmodeled fields. The node's serialized header and block hash rules remain specific to PoCX.

The source computes active-chain block confirmations as `tip height - block height + 1`. `getblock` reports `-1` for a block outside the main chain. Raw transaction lookup behaves differently: when a matching block is specified it returns `in_active_chain`; transaction JSON supplies confirmations when available. A transaction in the mempool is not confirmed, and a zero count is never treated here as settled.

`gettransaction` returns only in-wallet transactions, wallet amounts/fees/categories/details, raw hex, and optionally `decoded`; it is not a substitute for `getrawtransaction`. The client requires an explicit wallet name for this optional method and will not route to an automatically selected wallet. No wallet RPC was called during implementation or tests.

## Authentication and network safety

Configuration section: `BTCX:RPC`. On .NET configuration providers, environment variables can provide secrets, for example:

```text
BTCX__RPC__Endpoint=http://127.0.0.1:18443/
BTCX__RPC__CookieFilePath=/run/secrets/btcx-rpc.cookie
```

Alternatively inject `BTCX__RPC__Username` and `BTCX__RPC__Password` from a secret store. The node cookie is read for each request so node restart/cookie rotation does not require reconstructing the client. Credentials are never embedded in the endpoint URL or source. Authentication failures and RPC errors omit response bodies and server error strings to avoid credential reflection in logs.

The transport rejects endpoints containing URL credentials, a non-root path, query, or fragment. Literal public IP endpoints are rejected during options validation. DNS names are resolved by the socket connect callback and only loopback, RFC1918, IPv4 link-local, IPv6 ULA, or IPv6 link-local addresses are eligible; it connects to the checked address directly. HTTP proxy use and redirects are disabled. Thus a public IP returned by DNS is not used. Deploy RPC on loopback/private interfaces with node-side `rpcbind`/`rpcallowip` restrictions as well; this client is an additional outbound restriction, not a replacement for node firewall configuration.

The unconfigured endpoint default is loopback BTCX regtest (`127.0.0.1:18443`). Default timeout is 10 seconds. Up to two retries follow transport failures, request timeouts, HTTP 5xx responses without a valid RPC error envelope, or Bitcoin Core warmup RPC error `-28`; authentication failures and other RPC errors are not retried. Caller cancellation is propagated immediately. Responses are parsed without logging or echoing their content.

## Tests

`BtcxRpcClientTests` uses only mock `HttpMessageHandler` responses and covers connection/request/auth headers, cookie authentication, unauthorized responses, `getblockchaininfo`, `getnetworkinfo`, `getbestblockhash`, `getblockcount`, PoCX `getblock`, wallet-scoped `gettransaction`, raw and decoded transaction lookup, mempool confirmation semantics, malformed response handling, timeout retry, transport unavailability retry, HTTP 503 retry/recovery, caller cancellation, and public endpoint rejection.

No test connects to a BTCX production node or wallet. This stage does not include live regtest RPC validation; that requires an isolated node fixture in the subsequent integration work.
