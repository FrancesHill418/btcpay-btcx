# BTCX BTCPay staging package

This package starts an isolated BTCX regtest stack containing BTCPay Server v2.4.4 with this repository's BTCX plugin, Bitcoin-PoCX node and wallet RPC, electrs-btcx (with bindex-btcx built into it), and PostgreSQL. It is for development/staging only. It does not include Phoenix; Phoenix is a client wallet. It does not include XBoard itself; the pinned provider patch and instructions are under `integrations/xboard`.

**Do not connect this package to BTCX mainnet, production services, or production credentials. Do not send mainnet funds.** The plugin build currently rejects mainnet wallet address allocation. The PoCX REST patch in this stack is a development compatibility bridge, not a production-approved node release.

## Requirements

- Linux host with Docker Engine and Docker Compose v2.
- Git with submodule support; outbound access to fetch the pinned public upstream source during the first Docker build.
- Enough disk and memory to compile Bitcoin-PoCX, electrs-btcx, and the BTCX plugin. Initial `docker compose up -d` builds these images and can take a while.
- A free, non-overlapping Docker subnet. The example uses `172.30.50.0/24`; change it in `.env` if this overlaps the host's VPN, LAN, or Docker networks. The RPC allow-list must match that entire private backend subnet.

## Start the stack

From a clean checkout of the release package:

```sh
git submodule update --init --recursive
cp .env.example .env
./integrations/staging/init-secrets.sh
docker compose up -d
```

On first start, Compose builds from fixed source revisions, applies the preserved development-only PoCX `/rest/blockpart` compatibility patch, creates the regtest node, initializes the `btcx-receive` wallet, builds the electrs image with bindex-btcx, initializes PostgreSQL, and launches BTCPay with the BTCX plugin. Subsequent `docker compose up -d` starts/reconciles those same services; rebuild after a source or Dockerfile change with `docker compose build`.

The UI binds to `http://127.0.0.1:49392` by default. Create the first BTCPay administrator while bound to localhost, then set `BTCPAY_DISABLE_REGISTRATION=true` in `.env` and recreate BTCPay:

```sh
docker compose up -d --force-recreate btcpay
```

The web port can be changed through `STAGING_HTTP_BIND` and `STAGING_HTTP_PORT`. Keep it on loopback unless a reviewed staging reverse proxy/TLS endpoint is used. BTCX RPC, REST, P2P and Electrum have no published host ports. They share only the internal `btcx-staging-backend` network with BTCPay/PostgreSQL. The isolated RPC cookie is stored in its own Docker volume and mounted read-only in BTCPay and electrs; the node wallet/datadir is never mounted into BTCPay.

## Check status and fund regtest

```sh
docker compose ps
curl --fail http://127.0.0.1:49392/api/v1/health
docker compose exec bitcoin-pocx bitcoin-cli -regtest -rpccookiefile=/run/btcx-rpc/.cookie getblockchaininfo
docker compose exec bitcoin-pocx bitcoin-cli -regtest -rpccookiefile=/run/btcx-rpc/.cookie -rpcwallet=btcx-receive getnewaddress
```

Generate a disposable regtest balance (regtest mining uses the pinned PoCX regtest implementation):

```sh
RECEIVE_ADDRESS="$(docker compose exec -T bitcoin-pocx bitcoin-cli -regtest -rpccookiefile=/run/btcx-rpc/.cookie -rpcwallet=btcx-receive getnewaddress)"
docker compose exec bitcoin-pocx bitcoin-cli -regtest -rpccookiefile=/run/btcx-rpc/.cookie -rpcwallet=btcx-receive generatetoaddress 101 "$RECEIVE_ADDRESS"
```

Then check [healthchecks](integrations/staging/HEALTHCHECKS.md) for index synchronization and proceed to payment setup below.

## BTCX invoice and payment

1. In BTCPay, create a store configured for CNY and open `/server/btcx` as the server administrator.
2. Enable BTCX and set a disposable manual `1 BTCX = X CNY` rate. Verify the store's `BTCX_CNY` rule uses `manualbtcx(BTCX_CNY)`. The invoice keeps its original rate snapshot after later edits.
3. Create a disposable CNY invoice using `BTCX-CHAIN`. Confirm checkout displays a regtest `rpocx` address and `btcx:` URI with the expected BTCX amount.
4. Send the exact amount to the invoice address using the regtest wallet RPC. Verify electrs history sees the transaction, BTCPay detects it, and the invoice reaches its configured confirmation state. See the [payment smoke guide](integrations/staging/HEALTHCHECKS.md#payment-path-smoke).

## Connect the pinned XBoard provider

XBoard runs separately. Check out commit `4f48e61a2cbc6db5338872b6bdb45ef954ec1256`, apply `integrations/xboard/0001-btcpay-btcx-provider.patch` with `git am`, and follow [the provider guide](integrations/xboard/README.md). For an isolated same-host Docker test, connect the XBoard container to the internal backend network without detaching it from its own network:

```sh
docker network connect --alias xboard btcx-staging-backend <xboard-container-name>
```

Configure XBoard's BTCPay endpoint as `http://btcpay:49392` and make its callback URL resolve as `http://xboard:<xboard-port>` from BTCPay. Keep the Greenfield API token and webhook HMAC secret in XBoard's secret store. The package does not provision XBoard or create those credentials. The previously recorded XBoard live E2E used separate development instances; running that same full order/webhook flow against this newly composed stack remains a staging acceptance step.

## Services and source pins

| Service | Release source |
|---|---|
| BTCPay + BTCX plugin | BTCPay Server `v2.4.4`; plugin built from this source package |
| bitcoin-pocx | `005bf0098e217b76a2627bfae458dff4f5718dd5`; bundled Bitcoin source `b88b852644f629cd5f25b3424d11b462462c24b3` plus development-only backport patch |
| bindex-btcx | `eda7c70660baa06affef464c7ea1e131c39304f1`, compiled as electrs dependency (not an independent daemon) |
| electrs-btcx | `2f78c63e20215e20944767f0901209c4d740fe5b` |
| PostgreSQL | `postgres:16-alpine` image; volume-backed staging database |

See [backup/restore](integrations/staging/BACKUP-RESTORE.md) and [healthchecks](integrations/staging/HEALTHCHECKS.md). `docker compose down` stops services and retains volumes. `docker compose down -v` destroys all stack state and must only be used when intentionally deleting this disposable staging environment.
