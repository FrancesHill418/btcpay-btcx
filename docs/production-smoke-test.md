# Production-like staging smoke test

This is a required pre-release test plan. It must run only on an isolated non-mainnet deployment; no production hostname, wallet, database, XBoard, token, webhook key, or real funds may be used.

The earlier BTCX staging acceptance passed: Greenfield CNY invoice, real 61.7 BTCX regtest payment, six confirmations, `InvoiceSettled`, and XBoard order paid. That was against the prior staging stack. **This exact smoke has not yet been run against the new production Dockerfiles and final BTCPay generator output**, so it remains a release gate.

## Candidate setup

1. Build and publish candidate Bitcoin-PoCX/electrs images from `integrations/production` and record immutable digests. Generate an isolated BTCPay v2.4.4 stack from the BTCX fragment.
2. Use a unique Compose project, private subnet, PostgreSQL credentials, RPC cookie volume, BTCX regtest wallet, BTCPay admin/store and XBoard database. Confirm all secret files are test-only and mode-restricted.
3. Configure `BTCX_NETWORK=regtest`, `BTCX_RPC_PORT=18443`, `BTCX_P2P_PORT=18444`, `BTCX_ELECTRS_NETWORK=regtest`, `BTCX_ELECTRUM_PORT=60401`, `BTCX_WALLET_NETWORK=regtest`, and `BTCX_ALLOW_MAINNET=false`. Never point a test endpoint at mainnet.
4. Install the built `.btcpay` plugin through supported plugin installation; verify its identifier/version is loaded. Do not use `DEBUG_PLUGINS`.
5. Create and load a disposable regtest wallet manually through the node RPC; no standalone fake wallet service is used. Confirm RPC cookie and wallet data persist in their test volumes.

## Payment and webhook flow

1. Create an isolated CNY BTCPay store and enable `BTCX-CHAIN`; configure `BTCX_CNY` to `manualbtcx(BTCX_CNY)`. For a deterministic fixture only, set `1 BTCX = 0.20 CNY`.
2. Apply the provider-only patch to the exact XBoard test revision and configure isolated Greenfield/HMAC secret files.
3. Create an XBoard test order for CNY 12.34. Verify Greenfield metadata contains the expected `orderId`, BTCPay uses BTCX, and the invoice snapshot holds CNY amount, 61.7 BTCX, rate 0.20, source `manual`, and timestamp. A subsequent rate update must not reprice this invoice.
4. Verify checkout destination, BTCX URI and QR. Pay exactly 61.7 BTCX from the regtest wallet to the invoice address. Do not use any mainnet wallet or funds.
5. Confirm Electrum history discovers the transaction; Bitcoin-PoCX RPC verifies its txid, exact output script/amount and canonical block. Confirm the configured LowSpeed policy settles at six confirmations.
6. Verify actual signed BTCPay `InvoiceSettled` delivery to XBoard over HTTPS using the test HMAC secret. Check signature, event, invoice ID, `metadata.orderId`, CNY/BTCX amounts, currencies, payment method and settled state. Deliver a real duplicate/retry and confirm idempotent XBoard paid state.
7. Record service health, node/index heights, invoice/order/tx IDs, confirmations, webhook delivery and result. Redact credentials/secrets. Run the documented failure cases (underpayment, overpayment, duplicate, expiry) in this isolated environment.

## Acceptance gate

Pass requires all four services healthy, plugin loaded, node RPC and REST responding, Electrum response and caught-up index, exact payment observed, six-confirmation settlement, signed webhook accepted, duplicate safe, XBoard order paid, and failure cases with expected outcomes. Save the generated Compose and image/patch locks as non-secret release evidence. Do not label production ready based only on the historical staging E2E.
