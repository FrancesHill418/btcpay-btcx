# BTCX BTCPay staging deployment

This runbook describes an isolated, repeatable **regtest staging** deployment from the `development-complete` source tag. It is not a production/mainnet procedure. Never attach production credentials, customer data, a mainnet wallet, or mainnet chain data to this environment.

## Pinned source set

| Component | Version / revision |
|---|---|
| Release source baseline | Git tag `development-complete` (baseline commit `57995b3ecc154069c94d06c967cbac2c54ab3324`) |
| BTCPay Server | `v2.4.4`, commit `2d5a0d8077bb33af080e949031da33d84b80638d` |
| .NET SDK | `10.0.401`, `latestPatch` roll-forward |
| Bitcoin-PoCX | commit `005bf0098e217b76a2627bfae458dff4f5718dd5`; bundled Bitcoin source is v30.2.1, commit `b88b852644f629cd5f25b3424d11b462462c24b3` |
| bindex-btcx | commit `eda7c70660baa06affef464c7ea1e131c39304f1` |
| electrs-btcx | tag `v0.11.1-btcx.1`, commit `2f78c63e20215e20944767f0901209c4d740fe5b` |
| XBoard source for provider integration | commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256` |

The pinned, unmodified Bitcoin-PoCX source lacks `/rest/blockpart/`, which the pinned bindex/electrs startup probe requires. The repository contains `integrations/electrs-btcx/bitcoin-pocx-v30-blockpart-compat.patch`, a development-only backport tested on isolated regtest. Rebuild the node from the exact pinned source plus this patch for staging validation. This compatibility patch is not an upstream release and is **not approved as a production artifact**; revalidate it against a supported upstream version before any production review. Do not change consensus rules.

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

The configured node must provide a dedicated loaded or creatable `btcx-receive` wallet. Create/initialize it only on regtest. The wallet allocation code in this development build explicitly rejects `Network=main`; this staging runbook must remain on `regtest`.

For this PoCX build, regtest defaults are RPC `18443`, P2P `18444`, and address HRP `rpocx`; confirm actual node configuration before connecting. Electrs commonly uses TCP `50001` internally; configure the actual service to match `BTCX__Electrum__Endpoint`.

## Manual BTCX/CNY rate

After plugin load, an authorized BTCPay server administrator visits `/server/btcx`, enables BTCX, and enters `1 BTCX = X CNY`. The first release uses **manual** pricing only; there is no live exchange or Observatory feed. Save through the UI so the plugin records the UTC update timestamp and `manual` source. Confirm the store's `BTCX_CNY` rate rule selects `manualbtcx(BTCX_CNY)` and has no alternate fallback. Create a disposable invoice and verify its BTCX amount and immutable rate/timestamp snapshot before payment. Rate edits affect new invoices, not existing invoice quotes.

## XBoard provider patch and webhook

On a separate development XBoard instance, check out the pinned source commit and apply the preserved provider patch:

```sh
git checkout 4f48e61a2cbc6db5338872b6bdb45ef954ec1256
git am /path/to/btcpay-btcx/integrations/xboard/0001-btcpay-btcx-provider.patch
```

The provider patch selects Greenfield payment method `BTCX-CHAIN`, creates a CNY invoice with `metadata.orderId`, and binds the order to the invoice. Configure the development BTCPay URL, Store ID, a least-privilege Greenfield token (invoice create/view and webhook view/create/update), and a separate random webhook HMAC secret in the XBoard secret store. Use HTTPS except for isolated loopback/private development endpoints where the provider explicitly allows HTTP. Register `InvoiceSettled`; confirm signature verification, invoice/order binding, amount/currency, idempotency, and a real HTTP 200 callback with disposable orders. Never paste token or HMAC secret into `.env.example`, command history, logs, or this repository.

The patch's automated provider tests cover duplicate delivery, underpayment, overpayment, expiry, invalid signatures, and invoice/order binding. The isolated live E2E validated exact-payment order completion. Live under/overpayment, expiry, and duplicate webhook delivery were not run as end-to-end transactions; retain that distinction in the staging record.

## Staging acceptance and cleanup

Use [release-checklist.md](release-checklist.md) and record image digests, source commits, plugin artifact checksum, test invoice IDs, regtest transaction IDs, confirmation depth, webhook outcome, and operator/time. Do not record credentials or seeds. Verify runtime data resides only in the named staging volumes. Before decommissioning, stop the services and use the backup runbook if the test data must be retained; otherwise remove only the specifically identified staging environment through its normal owner-controlled lifecycle.

Phoenix PoCX v2.4.0 parser fixture compatibility is confirmed, but real device QR scan/send/receive remains pending. Do not mark staging device E2E complete based on parser fixtures.
