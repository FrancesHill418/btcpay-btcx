# BTCX BTCPay staging deployment

This runbook describes an isolated, repeatable **regtest staging** deployment from the `development-complete` source tag. It is not a production/mainnet procedure. Never attach production credentials, customer data, a mainnet wallet, or mainnet chain data to this environment.

The root Compose file is now a runtime lock manifest: each service image is pinned to a digest, and the custom BTCX images are local-only. This checkout does not publish those images or include a Compose build recipe. Do not use the file to replace/recreate the already-running acceptance stack or on a different host until image provenance and the running PoCX image drift in [image-lock.md](image-lock.md) are resolved. The current services were left running unchanged during production hardening.

## Pinned source set

| Component | Version / revision |
|---|---|
| Release source baseline | Git tag `development-complete` (baseline commit `57995b3ecc154069c94d06c967cbac2c54ab3324`) |
| BTCPay Server | `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` |
| .NET SDK | `10.0.401`, roll-forward `disable` |
| Bitcoin-PoCX | commit `005bf0098e217b76a2627bfae458dff4f5718dd5`; bundled Bitcoin source is v30.2.1, commit `b88b852644f629cd5f25b3424d11b462462c24b3` |
| bindex-btcx | commit `eda7c70660baa06affef464c7ea1e131c39304f1` |
| electrs-btcx | tag `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` |
| XBoard source for provider integration | commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` |

The pinned, unmodified Bitcoin-PoCX source lacks `/rest/blockpart/`, which the pinned bindex/electrs startup probe requires. The repository contains `integrations/electrs-btcx/bitcoin-pocx-v30-blockpart-compat.patch` and `integrations/electrs-btcx/bitcoin-pocx-v30-net-processing-compat.patch`, development-only compatibility backports tested on isolated regtest. The staging node Dockerfile applies both patches to the exact pinned source. They are not upstream releases and are **not approved as production artifacts**; revalidate against a supported upstream version before any production review. Do not change consensus rules.

## Build the plugin artifact

Use a clean checkout/worktree at the tag (do not build an uncommitted tree for a release candidate):

```sh
git clone --recurse-submodules <private-repository-url> btcpay-btcx
cd btcpay-btcx
git checkout development-complete
git submodule update --init --recursive
dotnet --version
dotnet restore
dotnet build -c Release --no-restore
dotnet publish src/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.csproj \
  -c Release --no-restore -o artifacts/BTCPayServer.Plugins.BTCX
```

The expected plugin files include `BTCPayServer.Plugins.BTCX.dll` and `BTCPayServer.Plugins.BTCX.json`. The publish command was run successfully against this release baseline. Record the source commit and a SHA-256 checksum of the artifact in the staging change record. Do not copy build outputs, runtime configuration, wallets, or chain data into the source repository.

Install the artifact into the persistent BTCPay plugin directory using BTCPay's plugin directory convention:

```text
<plugin-directory>/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.dll
<plugin-directory>/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.json
```

Mount the plugin directory read-only into the BTCPay container at `/plugins` and configure BTCPay with `--plugindir=/plugins` (or the equivalent supported configuration for the pinned image). Restart/recreate the staging BTCPay service, then verify the plugin is loaded and the BTCX settings page is available at `/server/btcx`. Preserve the plugin directory outside the ephemeral container layer. This repository does not contain a production-ready BTCPay Compose stack; integrate the mount through the operator's reviewed BTCPay custom Compose fragment. Consult [BTCPay plugin documentation](https://docs.btcpayserver.org/Development/Plugins/) and [Docker customization](https://docs.btcpayserver.org/Docker/customization/).

## Network and services

Use a dedicated development host and a private Docker network (for example `btcx-backend`) shared only by BTCPay, `bitcoin-pocx`, and `electrs-btcx`. Attach BTCPay to its normal frontend network as required, and additionally to this backend network. Give the node and indexer stable Docker DNS aliases such as `bitcoin-pocx` and `electrs-btcx`.

* Do not publish BTCX RPC, node REST, P2P, or Electrum ports on a public host interface. Keep them reachable only over the private backend network; enforce host firewall rules too.
* The node's RPC and REST API use its RPC HTTP listener. For a regtest container, a typical internal configuration uses `regtest=1`, `server=1`, `rest=1`, `txindex=1`, `rpcbind=0.0.0.0`, and `rpcallowip=<the exact backend subnet>`. Use a strong operator-generated `rpcauth` entry and inject its matching password into the BTCPay process through the host's secret manager. Never put credentials in a checked-in Compose file. Do not use broad `0.0.0.0/0` allow rules.
* For an isolated regtest, keep P2P private as well; do not add public peer exposure. Node ports and paths below are container-internal examples and must match the chosen node image/configuration.
* Configure electrs/bindex to use the same regtest node, its private RPC/REST service, and its own persistent **staging-only** index data. Expose the Electrum TCP listener only on `btcx-backend`.
* In this package, `electrs-btcx` uses `network_mode: service:bitcoin-pocx`. It shares the node's network namespace so bindex's localhost REST requests reach Bitcoin-PoCX. The Compose service DNS name `bitcoin-pocx` also resolves to that node address from BTCPay and the shared namespace. Keep electrs' `--daemon-rpc-addr` and the bindex REST localhost behavior aligned with this topology; the Electrum healthcheck probes `127.0.0.1:60401` in the shared namespace.
* The node, electrs index, BTCPay database, and wallet data belong in separate operator-managed staging volumes. These volumes are runtime state, not release contents or backup-free disposable artifacts.

## Plugin settings

Inject configuration into the BTCPay container using its deployment secret mechanism. The plugin binds `BTCX:RPC`, `BTCX:Wallet`, and `BTCX:Electrum`; double underscores map environment variables to nested keys. The following values are safe examples, but the password placeholder must be supplied externally:

```text
BTCX__RPC__Endpoint=http://bitcoin-pocx:18443/
BTCX__RPC__Username=btcpay-btcx
BTCX__RPC__Password=<inject-from-staging-secret-store>
BTCX__Wallet__Network=regtest
BTCX__Wallet__WalletName=btcx-receive
BTCX__Electrum__Enabled=true
BTCX__Electrum__Endpoint=tcp://electrs-btcx:50001/
BTCX__Electrum__PollIntervalSeconds=15
```

Alternatively configure `BTCX__RPC__CookieFilePath` and mount only the required cookie file read-only at that path, with ownership and renewal handled safely across node restarts. Choose either cookie authentication or username/password, never both. Do not mount the entire wallet/node datadir into BTCPay. The plugin rejects literal public RPC/Electrum IPs and validates resolved endpoint addresses; node-side bind/allow-list and network isolation remain required.

The configured node must provide a dedicated loaded or creatable `btcx-receive` wallet. Create/initialize it only on regtest. The wallet options now expose `BTCX__Wallet__AllowMainnet=false` by default; this staging runbook must remain on `regtest` and must not enable mainnet.

For this PoCX build, regtest defaults are RPC `18443`, P2P `18444`, and address HRP `rpocx`; confirm actual node configuration before connecting. Electrs commonly uses TCP `50001` internally; configure the actual service to match `BTCX__Electrum__Endpoint`.

## Manual BTCX/CNY rate

After plugin load, an authorized BTCPay server administrator visits `/server/btcx`, enables BTCX, and enters `1 BTCX = X CNY`. The first release uses **manual** pricing only; there is no live exchange or Observatory feed. Save through the UI so the plugin records the UTC update timestamp and `manual` source. Confirm the store's `BTCX_CNY` rate rule selects `manualbtcx(BTCX_CNY)` and has no alternate fallback. Create a disposable invoice and verify its BTCX amount and immutable rate/timestamp snapshot before payment. Rate edits affect new invoices, not existing invoice quotes.

## XBoard standalone provider and webhook

On a separate development XBoard instance, copy [`integrations/xboard/BtcpayBtcx`](../integrations/xboard/BtcpayBtcx) into `XBoard/plugins/BtcpayBtcx/`, then install and enable plugin code `btcpay_btcx` through XBoard's plugin manager. Do not apply the old patch or modify `plugins-core/Btcpay/Plugin.php`.

The independent `BTCPayBTCX` method creates a CNY invoice with `metadata.orderId`, forces BTCPay `BTCX-CHAIN`, verifies the BTCPay invoice's manual rate and BTCX amount snapshot, and stores an immutable order binding. Configure its separate development URL, Store ID, rate, least-privilege Greenfield token file, webhook HMAC secret file, and `allow_mainnet=false`. Use read-only `/run/secrets` mounts. The provider registers only `InvoiceSettled` at XBoard's normal `/api/v1/guest/payment/notify/BTCPayBTCX/{uuid}` callback; validate signature, invoice/order binding, amounts, currency, idempotency, and HTTP 200 with disposable orders. The original `BTCPay` payment channel remains independently available. Never paste token or HMAC secret into `.env.example`, command history, logs, or this repository.

The standalone provider tests are under `BtcpayBtcx/Tests`; they cover duplicate delivery, underpayment, overpayment, expiry, invalid signatures, invoice/order binding, and coexistence with the original provider. The earlier patch-based E2E is historical and does not establish this standalone integration's behavior; see the new standalone run below. Live under/overpayment and expiry were not run as end-to-end transactions.

### 2026-09-29 standalone BtcpayBtcx E2E

The standalone `plugins/BtcpayBtcx` plugin was installed and enabled in the isolated staging XBoard at commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`. `BTCPay` and `BTCPayBTCX` were both present; the original `plugins-core/Btcpay/Plugin.php` and `config.json` matched that upstream commit byte-for-byte. An XBoard CNY 12.34 order created BTCPay invoice `EPtMs7UrPBZKC98kKWHg4E`, with the BTCPay BTCX prompt and XBoard binding both snapshotting rate 0.20 CNY/BTCX and amount 61.7 BTCX on regtest. Regtest transaction `18e454e3af7310e388942e01a3ca8d4f25aeade13e5e4d847856ce4ba990e13e` was seen and confirmed after six generated regtest blocks. BTCPay reported the invoice `Settled` with CNY 12.34 paid and a settled 61.7 BTCX payment; the XBoard order reached status 3 and its BtcpayBtcx delivery ledger contained one row. Replaying a validly signed callback with the same delivery ID returned HTTP 200 and `success`, while the order remained status 3 and the ledger stayed at one row. This validates only isolated regtest staging, not mainnet or production.

## Staging acceptance and cleanup

Use [release-checklist.md](release-checklist.md) and record image digests, source commits, plugin artifact checksum, test invoice IDs, regtest transaction IDs, confirmation depth, webhook outcome, and operator/time. Do not record credentials or seeds. Verify runtime data resides only in the named staging volumes. Before decommissioning, stop the services and use the backup runbook if the test data must be retained; otherwise remove only the specifically identified staging environment through its normal owner-controlled lifecycle.

Phoenix PoCX v2.4.0 parser fixture compatibility is confirmed, but real device QR scan/send/receive remains pending. Do not mark staging device E2E complete based on parser fixtures.

### 2026-09-28 current Compose validation record

Compose resolved these services: `bitcoin-pocx`, `electrs-btcx`, `postgres`, `wallet-init`, and `btcpay`. The backend network is the internal `btcx-staging-backend`; electrs shares the Bitcoin-PoCX network namespace. From both the node's `127.0.0.1` listener and the electrs container, `GET /rest/chaininfo.json` returned HTTP 200 (the electrs container also reached `http://bitcoin-pocx:18443/rest/chaininfo.json`, HTTP 200). `server.version` succeeded and `blockchain.headers.subscribe` reported height 102, matching node `getblockchaininfo`. The wallet `btcx-receive` was loaded. PostgreSQL and BTCPay healthchecks passed; BTCPay logs confirmed plugin `BTCPayServer.Plugins.BTCX` 0.1.0 loaded.

This is a **partial** staging validation. No invoice, BTCX payment, confirmation, or current-stack XBoard callback was exercised. The BTCPay datadir was newly initialized and XBoard is external to this Compose package and was not running. Do not mark the staging release ready until a disposable CNY order completes the full Greenfield → BTCX regtest payment → signed `InvoiceSettled` callback → paid XBoard order flow against this BTCPay instance.

### Initial staging E2E attempt (2026-09-28) — BLOCKED, resolved below

This update supersedes the earlier statement that no development store existed. The active Compose environment was retained and used; it was not rebuilt or reset. `bitcoin-pocx`, `electrs-btcx`, `postgres`, and `btcpay` reported healthy. The node/electrs REST endpoint and Electrum protocol checks passed, with node and indexer both at height 102. BTCPay loaded BTCX plugin 0.1.0, and `/server/btcx` responded successfully.

An isolated BTCPay development store named `BTCX Development Staging` was created: Store ID `2VfNxGeK1aaQEVitL4TvY5HcVbaK1wK9FPxzxmUGeebm`. Its default currency is CNY. The plugin manual rate was enabled at `1 BTCX = 0.20 CNY`; Greenfield `BTCX_CNY` rates returned `0.20`. XBoard commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` was checked out separately, the preserved provider patch was applied, and an isolated SQLite-backed staging XBoard plus private-network Redis were started. No production database, XBoard endpoint, credentials, or webhook secret was used.

The actual XBoard HTTP checkout attempted a CNY 12.34 one-time order through the BTCPay provider. Greenfield invoice creation failed with BTCPay response `No wallet has been linked to your BTCPay Store`. A direct Greenfield invoice request against that store returned the same wallet-setup error. Therefore the following final-deployment checks are **FAIL / NOT RUN**: XBoard checkoutLink/invoice metadata readback, BTCX invoice/address, real 61.7 BTCX regtest send, listener confirmations, invoice settlement, actual BTCPay webhook delivery, and XBoard paid state. The store and manual rate exist, but BTCX checkout is not acceptance-ready until the development store has a supported BTCX on-chain wallet linked and the complete real-payment flow is rerun.

The XBoard provider PHPUnit suite passed 6 tests / 26 assertions. It covers duplicate delivery, expiry, underpayment, overpayment, signature/event validation, and invoice/order binding through its automated fixtures; these results did not replace the blocked live positive E2E at that checkpoint. No fake callback was used as positive-flow evidence. No BTCX mainnet or real funds were accessed. The blocker was resolved in the following staging run.

### Wallet linking resolution and final E2E (2026-09-28) — PASS

The initial generic Greenfield error was caused by the development store having no enabled BTCPay payment-method configuration. The BTCPay v2.4.4 invoice controller emits “No wallet has been linked” when no payment-method configs are registered. The existing BTCX plugin does not link an xpub/watch-only wallet to each store: it uses its server-side `BTCX:RPC` cookie-authenticated endpoint and `BTCX:Wallet` options (`WalletName=btcx-receive`, `Network=regtest`) to allocate invoice-labelled addresses with PoCX wallet RPC `getaddressesbylabel` / `getnewaddress`. `getwalletinfo` showed `private_keys_enabled=true`; this is the isolated regtest signing wallet, not watch-only and not mainnet.

Using the BTCPay Greenfield store payment-method API (no database edits), the staging store was configured with `PUT /api/v1/stores/{storeId}/payment-methods/BTCX-CHAIN` and `{"enabled":true,"config":{}}`. The plugin's config validator accepts its intentionally empty per-store config. BTCPay then reported `BTCX-CHAIN` activated and the invoice prompt was created through the plugin's existing node-RPC wallet integration. The temporary API key used only for this store configuration was kept outside the repository and is not documented here.

The actual XBoard order `2026092823095172496666658` created invoice `Ck9zoSuwTrwCUUTQFvurjz`: amount `12.34 CNY`, `metadata.orderId` matching that trade number, checkout method `BTCX-CHAIN`, and `LowSpeed`. Checkout returned a link and displayed `61.70000000 BTCX` at the configured rate `0.20 CNY/BTCX`. The receive address was `rpocx1qeaq44d8mpe29r3x3554sja9uus2ymgsrdxn8ft`; the checkout's `btcx:` payment URI and its QR component both used `btcx:rpocx1qeaq44d8mpe29r3x3554sja9uus2ymgsrdxn8ft?amount=61.7`.

From the existing PoCX wallet, regtest transaction `84e204e54c15b94fe847d24d13cb1c58c2d8dcb980c0d491fe73ca68a1c8314e` paid exactly 61.7 BTCX to that address. Electrs and the node reached height 209. The BTCX listener recorded the payment and six confirmations changed the invoice from `Processing` to `Settled` with `paidAmount=12.34 CNY`. BTCPay's configured actual webhook delivery for `InvoiceSettled` returned HTTP 200. Its event invoice ID and `metadata.orderId` matched the invoice/order above; XBoard recorded that verified delivery and order status `3` (paid). No fake HTTP callback was used.

Final health check: `bitcoin-pocx`, `electrs-btcx`, PostgreSQL and BTCPay all healthy; the separate isolated XBoard staging and Redis containers remained running. No mainnet endpoint, funds or production XBoard were used. **Final staging E2E status: PASS.**
