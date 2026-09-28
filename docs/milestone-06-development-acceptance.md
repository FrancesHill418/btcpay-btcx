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
- At the time of this earlier checkpoint, live XBoard delivery/order completion remained unverified; see the final E2E update below.
- Phoenix protocol compatibility passed, but real device and QR/send/receive E2E did not run.
- Wallet restore passed in regtest, but encrypted/offline backup handling and service-restart recovery remain unverified.
- BTCX/CNY quote is a manually administered rate, not a verified market feed.
- The production node wallet/key boundary, webhook TLS and rotation, indexer operations, and reorg-after-fulfillment compensation policy remain manual review gates.

These are development acceptance results only. No deployment or mainnet connection was performed or authorized.

## Final E2E validation update (2026-09-28)

The stale XBoard/Phoenix status bullets above are superseded by this update. XBoard live E2E passed in isolated loopback services using XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`, BTCPay v2.4.4 and BTCX regtest. Real XBoard order `2026092817090198674274629` (CNY 12.34) created Greenfield invoice `M1pw6WxA1RZjFWTeveh4ct`, with matching `metadata.orderId`, BTCX-CHAIN, BTCX amount 61.7, quote snapshot rate 0.20, and a `btcx:` address/URI. Transaction `d1902feecaff154d8734f51c1e6238e867e1507971d860975d62061f40c331ab` was sent on regtest and detected by BTCPay. The invoice settled, BTCPay's actual InvoiceSettled webhook returned HTTP 200, and the XBoard order reached paid status (`status=3`) with callback invoice ID matching the invoice ID. The webhook code validates the BTCPay-Sig HMAC-SHA256 using constant-time comparison over the raw payload. No fake webhook was used for successful flow.

The live run exposed and fixed the provider's payment method ID: BTCPay Greenfield serves `BTCX-CHAIN`, not `BTCX-OnChain`. The patch and updated provider tests are preserved in `integrations/xboard/0001-btcpay-btcx-provider.patch`; provider tests passed (6 tests, 25 assertions).

Live duplicate webhook redelivery was not run because the BTCPay session needed for its UI-only redelivery route was unavailable; duplicate behavior remains covered by provider tests. Underpayment, overpayment and expiry were not exercised live in this XBoard run and remain non-live coverage.

Phoenix remains **PROTOCOL COMPATIBLE** by the pinned parser fixture, but **REAL DEVICE E2E VERIFIED: NO**. The host has no `adb`, no Flutter runtime, and no `/dev/bus/usb`; no Phoenix device/emulator scan/send/receive path could be run. No production environment, BTCX mainnet or mainnet funds were used. This unresolved device requirement means final development validation is incomplete.

## Final validation delta (2026-09-28)

- Latest `dotnet build` passed with 0 warnings/errors. Latest `dotnet test` passed 106, failed 0, skipped 1 gated runtime smoke; it requires `BTCX_RUNTIME_SMOKE=1` and an isolated development PostgreSQL. The explicitly gated runtime smoke was previously run and passed 107/107 with real regtest and electrs.
- After applying the compatibility patch, `dotnet restore` passed, `dotnet build --no-restore` passed with 0 warnings/errors, and the complete gated suite passed 107/107. The runtime smoke now connects to real electrs and real isolated PostgreSQL.
- The separate HTTP REST checks returned 200 for `/rest/chaininfo.json`, `/rest/blockhashbyheight/0.bin`, `/rest/block/<genesis>.bin`, `/rest/blockpart/<genesis>.bin?offset=0&size=491`, and `/rest/spenttxouts/<genesis>.bin`; invalid range returned 400 and an unknown hash returned 404. The electrs server answered `server.version` as `electrs/0.11.1` and indexed the real address/transaction described above.
- Final ordinary `dotnet restore && dotnet build --no-restore && dotnet test --no-build` passed restore/build (0 warnings/errors) and 106 tests; one runtime smoke was skipped by its default gate. The same full suite with isolated PostgreSQL, regtest node cookie and real electrs passed 107/107, 0 skipped.
- The following final update closes XBoard exact-payment delivery/order completion, while Phoenix device scan/send/receive remains unverified. The official `.ps1` testkit was not run because `pwsh` is unavailable; shell/curl and Electrum protocol checks covered equivalent node/indexer conditions.

## Final RELEASE PACKAGING staging E2E (2026-09-28) — BLOCKED

This is the final result for the current release Compose stack and supersedes any implication that the earlier isolated XBoard E2E validated this deployment.

- **Staging infrastructure: PASS.** Compose reports `bitcoin-pocx`, `electrs-btcx`, PostgreSQL, and BTCPay healthy. BTCPay loaded BTCX plugin 0.1.0. Bitcoin-PoCX REST answered from the node namespace and electrs namespace; Electrum `server.version` succeeded, and electrs/node heights matched at 102. The existing regtest wallet remained available.
- **electrs: PASS.** The running indexer answered through the shared node network namespace. The service DNS check to `bitcoin-pocx` also returned REST HTTP 200. No localhost healthcheck defect remained in this final stack.
- **BTCPay development store: PARTIAL.** Store `BTCX Development Staging`, ID `2VfNxGeK1aaQEVitL4TvY5HcVbaK1wK9FPxzxmUGeebm`, was created with CNY default currency. BTCX manual rate was enabled at `1 BTCX = 0.20 CNY`; the Greenfield rate endpoint returned 0.20. However, BTCPay's invoice creation rejected the store because no on-chain wallet is linked. Do not treat the BTCX payment path as enabled/ready for checkout.
- **XBoard staging: PARTIAL.** Exact commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` was checked out into a separate temporary worktree, and the preserved provider patch was applied. A separate SQLite database and private staging Redis were used. A staging-only XBoard payment record and CNY 12.34 test plan were configured. No production XBoard, database, credentials, or webhook secret was used.
- **Greenfield / real payment / webhook: FAIL.** XBoard's real HTTP checkout reached the provider, which attempted to create a CNY 12.34 invoice with BTCX-CHAIN terms. BTCPay returned `No wallet has been linked to your BTCPay Store`; a direct Greenfield invoice request reproduced the same result. Consequently no invoice ID/checkoutLink was returned, no expected 61.7 BTCX invoice/address was produced, and no regtest transaction, payment-listener confirmation, settled invoice, real BTCPay webhook, or paid XBoard order occurred. These steps are **NOT RUN**, not passed.
- **Negative automation: PASS (unit scope only).** `vendor/bin/phpunit tests/Unit/Plugins/BtcpayPluginTest.php --testdox`: 6 tests / 26 assertions passed. Existing fixtures cover duplicate delivery, expired/late invoice, underpayment, overpayment, invalid HMAC/event, and invoice/order/currency/payment-method mismatch. They do not demonstrate a duplicate delivery or failure mode in the blocked live staging path.
- **Remaining blocker:** complete supported BTCX wallet linking/configuration for this development store without changing BTCPay core or the provider architecture, then rerun the full real payment and actual webhook flow. Do not connect BTCX mainnet or send real funds.

**Release decision: STAGING RELEASE BLOCKED.**
