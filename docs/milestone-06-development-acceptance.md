# MILESTONE 06 — Development acceptance

Date: 2026-09-28. All runtime activity used isolated development paths, a loopback-only PoCX regtest node and a loopback PostgreSQL instance. No production service, credential or BTCX mainnet was accessed.

## Automated validation

- `dotnet restore BTCPayServer.Plugins.BTCX.slnx`: passed; dependencies already restored.
- `dotnet build BTCPayServer.Plugins.BTCX.slnx --no-restore`: passed, 0 warnings and 0 errors.
- Ordinary `dotnet test BTCPayServer.Plugins.BTCX.slnx --no-build`: 107 total, 106 passed, 0 failed, 1 gated runtime test skipped. The skipped test is separately covered below.
- Full gated `dotnet test` with `BTCPAY_RUNTIME_SMOKE=1`, `TESTS_POSTGRES` pointing at the isolated loopback PostgreSQL instance, `BTCX_RUNTIME_NODE_COOKIE` pointing at the isolated node cookie, and `BTCX_RUNTIME_ELECTRUM_ENDPOINT=tcp://127.0.0.1:50401`: 107 passed, 0 failed, 0 skipped. This run used the real electrs-btcx service described below, not the Electrum fixture. The test gate accepts both `BTCX_RUNTIME_SMOKE=1` and `BTCPAY_RUNTIME_SMOKE=1`.
- Gated runtime acceptance loaded BTCPay v2.4.4, created and retrieved a BTCX CNY invoice via Greenfield including payment methods, validated the saved quote (CNY 7.50, rate 0.20 CNY/BTCX, 37.5 BTCX), address and `btcx:` link, and rendered checkout. The plugin used authenticated RPC to the real BTCX regtest node and allocated a wallet address. A real 37.5 BTCX transaction was mined, persisted by the plugin sink, reached invoice Settled after six confirmations, reverted to payment Processing after block invalidation, and returned to Settled after reconsideration.
- This runtime smoke used real electrs discovery end to end: BTCPay allocated a receive address through the real node wallet RPC, the miner wallet sent the exact 37.5 BTCX payment, electrs discovered it, BTCPay persisted it, six confirmations settled it, and invalidate/reconsider changed and restored the payment state. A new `BTCX_RUNTIME_ELECTRUM_ENDPOINT` option selects an external development indexer; omitting it retains the protocol fixture path for the standalone regression run.
- XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` plus the provider patch was applied in an isolated checkout. PHPUnit: 6 tests / 26 assertions passed. PHP syntax checks passed. Tests cover CNY invoice terms, metadata/order binding, invoice reuse, HMAC, event type, duplicate delivery, expiry, under/overpayment, currency, method and order mismatch. HTTP interactions use fakes and SQLite; no live callback or XBoard order completion was exercised.
- Phoenix PoCX v2.4.0 `parsePaymentUri` was executed from its pinned source using the BTCPay regtest address/URI fixture. BTCX scheme, 37.5 BTCX, one atomic unit, regtest network identification and Bitcoin-address rejection passed. **PROTOCOL COMPATIBLE**. No Phoenix device/app, QR scan, or wallet transaction was run: **REAL DEVICE E2E VERIFIED: NO**.

## electrs-btcx compatibility result

The binaries are electrs `0.11.1`, pinned commit `2f78c63e20215e20944767f0901209c4d740fe5b`, and bindex-btcx commit `eda7c70660baa06affef464c7ea1e131c39304f1`. The pinned PoCX node is outer commit `005bf0098e217b76a2627bfae458dff4f5718dd5`, with Bitcoin v30.2.1 submodule commit `b88b852644f629cd5f25b3424d11b462462c24b3`. Initial unpatched startup failed with this exact diagnostic:

```text
GET http://localhost:18443/rest/blockpart/2a98a52253aeff06093948b00568d380b7634621bc606403127973c9acbbfde0.bin?offset=0&size=491 failed: StatusCode(404)
Error: electrs failed
Caused by:
    0: failed to open index
    1: use https://github.com/bitcoin/bitcoin/pull/33657
```

Direct endpoint checks against the exact regtest genesis proved that chaininfo, blockhashbyheight, full block and spenttxouts returned HTTP 200, while blockpart returned 404. The hash existed and REST was enabled, so the cause was a missing route (not an unknown hash, bad range, or disabled REST). The pinned PoCX source does not carry Bitcoin Core PR [#33657](https://github.com/bitcoin/bitcoin/pull/33657), which added the required REST partial-block read.

For development validation only, PR #33657 was backported to the pinned v30.2.1 source and its `util::Expected` result interface was adapted to the older v30 API. The compatibility patch is preserved in [integrations/electrs-btcx](../integrations/electrs-btcx/README.md). No consensus code or rules were changed. The isolated patched node built successfully; chaininfo, height-0 hash, full genesis block, blockpart, and spenttxouts all returned HTTP 200. Pinned electrs-btcx then synchronized regtest height 536 and served Electrum. A real 12.345 BTCX transaction was observed in mempool at height 0 and after mining at height 537; address history, UTXO and raw transaction queries succeeded. A second real 37.5 BTCX transaction passed through the plugin listener and BTCPay invoice lifecycle, including confirmation and reorg/reconfirmation. No RPC fallback listener was added. This verifies the dev compatibility patch with the pinned indexer; it does not establish compatibility with an unpatched official PoCX binary.

## Wallet recovery and security evidence

A development-only backup of `btcx-receive` was created with the node's `backupwallet`, mode 0600, and restored under a new wallet name. The source/restored wallets had equal transaction counts; all 9 labels and corresponding receive addresses matched, and all 5 transaction IDs in wallet history matched. The temporary backup file was removed after verification. This is a local regtest recovery test, not an encrypted/offline backup drill or a BTCPay process restart test.

RPC and PostgreSQL were bound to loopback. The smoke read the isolated development node cookie from its temporary datadir. No production credentials were present or used. No private-key material was added to the repository. The node wallet is spend-capable; its successful use here does not approve it as a production receiving-key boundary.

## Remaining acceptance limits

- Real electrs/bindex history, UTXO, mempool and confirmation passed against an isolated PoCX build carrying the documented REST backport. Indexer restart/recovery was not exercised.
- Live XBoard + BTCPay Greenfield webhook delivery and order completion remain unverified.
- Phoenix protocol compatibility passed, but real device and QR/send/receive E2E did not run.
- Wallet restore passed in regtest, but encrypted/offline backup handling and service-restart recovery remain unverified.
- BTCX/CNY quote is a manually administered rate, not a verified market feed.
- The production node wallet/key boundary, webhook TLS and rotation, indexer operations, and reorg-after-fulfillment compensation policy remain manual review gates.

These are development acceptance results only. No deployment or mainnet connection was performed or authorized.

## Final validation delta (2026-09-28)

- After applying the compatibility patch, `dotnet restore` passed, `dotnet build --no-restore` passed with 0 warnings/errors, and the complete gated suite passed 107/107. The runtime smoke now connects to real electrs and real isolated PostgreSQL.
- The separate HTTP REST checks returned 200 for `/rest/chaininfo.json`, `/rest/blockhashbyheight/0.bin`, `/rest/block/<genesis>.bin`, `/rest/blockpart/<genesis>.bin?offset=0&size=491`, and `/rest/spenttxouts/<genesis>.bin`; invalid range returned 400 and an unknown hash returned 404. The electrs server answered `server.version` as `electrs/0.11.1` and indexed the real address/transaction described above.
- Final ordinary `dotnet restore && dotnet build --no-restore && dotnet test --no-build` passed restore/build (0 warnings/errors) and 106 tests; one runtime smoke was skipped by its default gate. The same full suite with isolated PostgreSQL, regtest node cookie and real electrs passed 107/107, 0 skipped.
- XBoard live app-to-app delivery/order completion and Phoenix real-device scan/send/receive remain unverified; provider tests and parser fixture are the alternatives. The official `.ps1` testkit was not run because `pwsh` is unavailable; shell/curl and Electrum protocol checks covered equivalent node/indexer conditions.
