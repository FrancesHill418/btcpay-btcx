# MILESTONE 06 — Development acceptance

Date: 2026-09-28. All runtime activity used isolated development paths, a loopback-only PoCX regtest node and a loopback PostgreSQL instance. No production service, credential or BTCX mainnet was accessed.

## Automated validation

- `dotnet restore BTCPayServer.Plugins.BTCX.slnx`: passed; dependencies already restored.
- `dotnet build BTCPayServer.Plugins.BTCX.slnx --no-restore`: passed, 0 warnings and 0 errors.
- Ordinary `dotnet test BTCPayServer.Plugins.BTCX.slnx --no-build`: 107 total, 106 passed, 0 failed, 1 gated runtime test skipped. The skipped test is separately covered below.
- Full gated `dotnet test` with `BTCPAY_RUNTIME_SMOKE=1`, `TESTS_POSTGRES` pointing at the isolated loopback PostgreSQL instance, and `BTCX_RUNTIME_NODE_COOKIE` pointing to the isolated node cookie: 107 passed, 0 failed, 0 skipped. The test gate accepts both `BTCX_RUNTIME_SMOKE=1` and the documented alias `BTCPAY_RUNTIME_SMOKE=1`.
- Gated runtime acceptance loaded BTCPay v2.4.4, created and retrieved a BTCX CNY invoice via Greenfield including payment methods, validated the saved quote (CNY 7.50, rate 0.20 CNY/BTCX, 37.5 BTCX), address and `btcx:` link, and rendered checkout. The plugin used authenticated RPC to the real BTCX regtest node and allocated a wallet address. A real 37.5 BTCX transaction was mined, persisted by the plugin sink, reached invoice Settled after six confirmations, reverted to payment Processing after block invalidation, and returned to Settled after reconsideration.
- Discovery limitation: this smoke fed the real txid through a local Electrum protocol fixture. It verifies node RPC, wallet allocation, payment sink, confirmation, BTCPay persistence and reorg handling, but does not verify electrs indexing.
- XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` plus the provider patch was applied in an isolated checkout. PHPUnit: 6 tests / 26 assertions passed. PHP syntax checks passed. Tests cover CNY invoice terms, metadata/order binding, invoice reuse, HMAC, event type, duplicate delivery, expiry, under/overpayment, currency, method and order mismatch. HTTP interactions use fakes and SQLite; no live callback or XBoard order completion was exercised.
- Phoenix PoCX v2.4.0 `parsePaymentUri` was executed from its pinned source using the BTCPay regtest address/URI fixture. BTCX scheme, 37.5 BTCX, one atomic unit, regtest network identification and Bitcoin-address rejection passed. **PROTOCOL COMPATIBLE**. No Phoenix device/app, QR scan, or wallet transaction was run: **REAL DEVICE E2E VERIFIED: NO**.

## electrs-btcx compatibility result

The existing binary is electrs `0.11.1`, pinned commit `2f78c63e20215e20944767f0901209c4d740fe5b`, with bindex-btcx commit `eda7c70660baa06affef464c7ea1e131c39304f1`. It connected to the isolated PoCX regtest node RPC at `127.0.0.1:18443` and served locally during initialization, then exited before indexing with this exact diagnostic:

```text
GET http://localhost:18443/rest/blockpart/2a98a52253aeff06093948b00568d380b7634621bc606403127973c9acbbfde0.bin?offset=0&size=491 failed: StatusCode(404)
Error: electrs failed
Caused by:
    0: failed to open index
    1: use https://github.com/bitcoin/bitcoin/pull/33657
```

Pinned bindex source `bindex-lib/src/chain.rs` probes `/rest/blockpart/` at startup and maps HTTP 404 to `NotSupported`. The PoCX daemon returned 404 for the endpoint. Bitcoin Core PR [#33657](https://github.com/bitcoin/bitcoin/pull/33657) added REST partial-block reads; this current PoCX build does not include that endpoint despite exposing the REST API needed by other bindex operations. This is a node/indexer compatibility failure, not an address or Electrum-protocol failure.

No RPC-based production fallback was added: adding one alongside Electrs would create two production listeners, and replacing the existing listener would require separate design and restart/reorg/mempool tests. Candidate follow-up options are (a) a PoCX node release carrying a compatible `/rest/blockpart/` implementation, (b) a reviewed PoCX patch/backport of the endpoint with consensus-aware tests, or (c) a separately evaluated single-backend RPC scanner. Do not claim indexed UTXO/history validation until one works against the pinned PoCX node.

## Wallet recovery and security evidence

A development-only backup of `btcx-receive` was created with the node's `backupwallet`, mode 0600, and restored under a new wallet name. The source/restored wallets had equal transaction counts; all 9 labels and corresponding receive addresses matched, and all 5 transaction IDs in wallet history matched. The temporary backup file was removed after verification. This is a local regtest recovery test, not an encrypted/offline backup drill or a BTCPay process restart test.

RPC and PostgreSQL were bound to loopback. The smoke read the isolated development node cookie from its temporary datadir. No production credentials were present or used. No private-key material was added to the repository. The node wallet is spend-capable; its successful use here does not approve it as a production receiving-key boundary.

## Remaining acceptance limits

- Real electrs/bindex history, UTXO and restart recovery against PoCX remain blocked by the missing REST endpoint.
- Live XBoard + BTCPay Greenfield webhook delivery and order completion remain unverified.
- Phoenix protocol compatibility passed, but real device and QR/send/receive E2E did not run.
- Wallet restore passed in regtest, but encrypted/offline backup handling and service-restart recovery remain unverified.
- BTCX/CNY quote is a manually administered rate, not a verified market feed.
- The production node wallet/key boundary, webhook TLS and rotation, indexer operations, and reorg-after-fulfillment compensation policy remain manual review gates.

These are development acceptance results only. No deployment or mainnet connection was performed or authorized.
