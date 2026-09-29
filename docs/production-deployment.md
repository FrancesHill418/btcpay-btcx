# BTCX production deployment procedure

**Release candidate procedure only. Production remains NO-GO until all gates below have independently reviewed evidence.** No production wallet is created, no mainnet RPC is contacted, and no deployment is performed by this document/task.

This package follows BTCPay's external-plugin plus coin-infrastructure model: [Altcoins](https://docs.btcpayserver.org/Development/Altcoins/) and [Plugins](https://docs.btcpayserver.org/Development/Plugins/). BTCX plugin code runs inside BTCPay; the runtime coin services are Bitcoin-PoCX (including wallet RPC) and electrs-btcx. PostgreSQL belongs to the BTCPay stack. The current listener depends on electrs for address-history discovery. No standalone wallet, bindex, listener, or Phoenix service is used.

## 0. Release gates

Do not proceed to a mainnet deployment until all are closed with evidence and approvals:

* approved source/toolchain build, package checksums, SBOM and vulnerability review;
* production registry images built from the committed Dockerfiles, pushed, inspected and pinned by immutable digests;
* reviewed Bitcoin-PoCX and electrs compatibility patch, upstream support decision and independent source review;
* `BTCPayServer.Plugins.BTCX.btcpay` installed and loaded on the exact BTCPay v2.4.4 release in an isolated staging stack;
* production wallet custody/backup restore drill, secrets manager, HTTPS, database recovery, reorg/operations policy and manual-rate governance approved;
* production-like staging smoke in [production-smoke-test.md](production-smoke-test.md) passed using release candidate images and the standalone XBoard `BtcpayBtcx` plugin;
* Phoenix real-device test status explicitly accepted by the release owner. Current project evidence says it is pending.

The current source references and open gates are summarized in [PROJECT-STATE.md](PROJECT-STATE.md), [image-lock.md](image-lock.md), and [production-security-checklist.md](production-security-checklist.md). Creating `v0.1.0-rc2` does not close them or authorize deployment.

## 1. Clean-machine prerequisites

1. Use a dedicated Linux host with supported Docker Engine and Docker Compose plugin. Install them using the official Docker instructions for that distribution; record exact package versions and secure the host before adding BTCPay.
2. Provision separate production host/DNS, storage, firewall, monitoring, backup destination and secret-manager namespace. Do not reuse staging resources or credentials.
3. Select a private Docker subnet that does not overlap host/VPN/cloud networks. Record it as `BTCX_BACKEND_SUBNET`.
4. Choose the public BTCPay hostname and configure DNS. Allow inbound HTTPS only through the approved TLS reverse proxy; the official generator's nginx profile manages certificates when that profile is selected. Never publish node RPC, REST, Electrum or PostgreSQL ports.

## 2. Clone deployment sources and prepare the generator

The repository does not currently use a private BTCPay Docker fork. Its source overlay and crypto-definition snapshot are under [`integrations/production/btcpayserver-docker`](../integrations/production/btcpayserver-docker/); the generator upstream commit is in `upstream-lock.json`. Establish an operator-controlled fork or get the integration accepted upstream before production. The deployment must not depend on untracked local edits or a floating `master` branch.

On an isolated packaging/staging host:

```sh
git clone https://github.com/btcpayserver/btcpayserver-docker.git
cd btcpayserver-docker
git checkout 9c8fe127850d079405dbb2db0748611548dc1a42
```

Apply the maintained overlay and patches to the pinned checkout:

```sh
/path/to/btcpay4btcx/integrations/production/btcpayserver-docker/apply-overlay.sh "$PWD"
```

For repeatable production, commit those changes in the approved fork and pin its commit. Do not edit `Generated/docker-compose.generated.yml`; generator inputs are the source of truth.

Build the Bitcoin-PoCX and electrs images from the repository root so Docker build contexts include the pinned sources and patches:

```sh
docker build --pull=false -f integrations/production/bitcoin-pocx/Dockerfile \
  -t registry.example.invalid/btcx/bitcoin-pocx:0.1.0-rc2 .
docker build --pull=false -f integrations/production/electrs/Dockerfile \
  -t registry.example.invalid/btcx/electrs-btcx:0.1.0-rc2 .
docker build --pull=false -f integrations/production/btcpay/Dockerfile \
  -t registry.example.invalid/btcx/btcpayserver:0.1.0-rc2 .
```

Run `docker image inspect` on the final published references, record registry manifest digest, image config ID, build arguments, source commits and patch hashes in the deployment lock record, then configure `BTCX_NODE_IMAGE` and `BTCX_ELECTRS_IMAGE` as `registry/name:tag@sha256:<manifest-digest>`. The example registry and digests above are deliberately not supplied: no production image has been published or assigned a real digest in this repository.

Build the plugin using [package-plugin.sh](../scripts/package-plugin.sh) and follow [plugin-release-package.md](plugin-release-package.md). It targets .NET 10 and BTCPay v2.4.4. Upload the `.btcpay` artifact using **Server Settings → Plugins → Upload** in a non-production validation instance and verify the plugin is listed/loaded. Do not configure `DEBUG_PLUGINS`; it is a local development mechanism only and is prohibited in production.

## 3. Secrets and environment

Start from [`integrations/production/.env.example`](../integrations/production/.env.example). Copy it into the operator's protected deployment directory, not Git. It contains only non-secret values and secret-file paths. Deliver secret contents from a managed secret provider or Docker secrets; do not put secret values in `.env`, Compose, Dockerfiles, command-line arguments, image layers, README or logs.

Required production secret material:

* PostgreSQL password: generated and mounted to PostgreSQL using the selected official BTCPay deployment secret mechanism;
* Bitcoin-PoCX RPC cookie: created by the daemon at runtime in a private volume and mounted read-only into BTCPay for the plugin's wallet RPC. electrs receives a separate runtime-generated `rpcauth` user with a per-user method whitelist and a distinct credential volume; the cookie is not mounted into electrs and no password is stored in Git;
* BTCPay Greenfield token: scoped to the production store, mounted as a read-only file into XBoard;
* XBoard webhook HMAC secret: independently generated and mounted as a different read-only file into XBoard;
* BTCPay key material persisted in its protected datadir/secret manager per the selected BTCPay deployment process. It is not injected by the BTCX fragment as a literal environment value.

Create separate values and ACLs for production and staging. Confirm BtcpayBtcx's provider fields contain only `/run/secrets/...` paths (`btcpay_btcx_api_key_file`, `btcpay_btcx_webhook_key_file`). RPC uses the daemon-generated cookie in this fragment, not username/password. Never display the cookie or token when checking mounts.

Set `BTCX_ALLOW_MAINNET=false` during configuration and verification. The plugin defaults mainnet to disabled. Enabling mainnet requires a separate authorized change after review; this package process must not turn it on automatically.

## 4. Generate and inspect the BTCPay stack

Use the official BTCPay Compose generator with the BTCX code selected as a crypto slot. For a mainnet production candidate, the explicit selection is:

```sh
export BTCPAYGEN_CRYPTO1=btcx
export NBITCOIN_NETWORK=mainnet
export BTCPAYGEN_LIGHTNING=none
export BTCPAYGEN_REVERSEPROXY=nginx
export BTCPAYGEN_EXCLUDE_FRAGMENTS='opt-add-tor,btcpay-host'
export BTCX_NETWORK=main
export BTCX_WALLET_NETWORK=main
export BTCX_ELECTRS_NETWORK=bitcoin
export BTCX_ALLOW_MAINNET=false
export BTCX_BACKEND_SUBNET='<approved-private-cidr>'
export BTCPAY_IMAGE='<immutable-btcpay-wrapper-image-reference>'
export BTCX_NODE_IMAGE='<immutable-node-image-reference>'
export BTCX_ELECTRS_IMAGE='<immutable-electrs-image-reference>'
dotnet run --project docker-compose-generator/src/docker-compose-generator.csproj \
  --configuration Release --no-launch-profile
python3 /path/to/btcpay4btcx/integrations/production/btcpayserver-docker/normalize-generated-compose.py \
  Generated/docker-compose.generated.yml
```

This direct generator invocation writes `Generated/docker-compose.generated.yml`. Use the official `. btcpay-setup.sh -i` deployment workflow only after separately authorized and after inspecting the generated artifact; when using `build.sh`, build its generator image from the same pinned deployment fork rather than pulling a mutable generator tag. Pin the .NET SDK/toolchain used for generation.

```sh
docker compose -f Generated/docker-compose.generated.yml config
```

Review the generated file and verify that it contains BTCPay Server, PostgreSQL, `bitcoin-pocx`, and `electrs-btcx`; the plugin is installed in BTCPay, not a separate service. Confirm only the HTTPS reverse proxy has host-published web ports. RPC/REST (8332), P2P (8333), Electrum (50001), and PostgreSQL (5432) have no host `ports:` mapping. Keep `Generated/` as generated output and regenerate it after any fragment change.

`docker compose config` is a structural check only; it does not prove images exist, can be pulled, have the claimed digest, or pass service health checks. The candidate image references remain unresolved until the image publication gate is closed.

## 5. First deployment steps (only after separate authorization)

These steps are intentionally not executed in this task:

1. Run the reviewed `docker compose ... up -d` command only after the release owner separately authorizes the production deployment.
2. Complete the first administrator registration promptly through the HTTPS hostname; disable public registration after setup.
3. Wait for the Bitcoin-PoCX mainnet node and electrs to synchronize and compare their heights. Confirm REST and Electrum health, and review disk/RPC/indexer alerts.
4. Under the approved custody ceremony, create a dedicated production receiving wallet named `btcx-production-receive` through Bitcoin-PoCX wallet RPC (for example `docker compose exec bitcoin-pocx bitcoin-cli createwallet btcx-production-receive`). Verify chain/genesis and wallet status with `getwalletinfo` and `getnewaddress`. Do not reuse or restore the staging wallet. Backup and restore requirements are in [production-backup.md](production-backup.md). This repository task will not create that wallet.
5. There is no separate BTCPay core wallet-link operation for the plugin's node RPC wallet. In the production store, enable the `BTCX-CHAIN` payment method; the plugin uses its server-configured wallet name and RPC endpoint for invoice address allocation. Confirm the wallet is loaded before enabling BTCX invoices.
6. Set the store currency to CNY and configure the store BTCX/CNY rule to the plugin's `manualbtcx(BTCX_CNY)` provider. Enable BTCX in `/server/btcx` and record the authorized value using the definition `1 BTCX = X CNY`. For example, `1 BTCX = 0.20 CNY` is a test value only, not a production rate recommendation.
7. Create a CNY invoice in a non-mainnet staging stack and inspect the persisted snapshot: CNY amount/currency, BTCX amount/currency, rate, source, and timestamp. A rate change applies to newly created invoices; old invoices keep their captured BTCX due amount until expiry.
8. Install the standalone [`BtcpayBtcx` XBoard plugin](../integrations/xboard/BtcpayBtcx/README.md) under `plugins/BtcpayBtcx`; do not patch `plugins-core/Btcpay`. Configure its production BTCPay base URL and store ID; mount the scoped Greenfield token and separate webhook HMAC file under `/run/secrets`. Never store secret values in XBoard settings or Compose.
9. Configure the XBoard notification URL as public HTTPS, register the matching secret with BTCPay's webhook configuration, subscribe to `InvoiceSettled`, and verify signature, invoice/order metadata, amounts, payment method and idempotent status handling in isolated staging.
10. Confirm BTCPay, node RPC+REST, electrs Electrum, PostgreSQL readiness, TLS expiry monitoring, and backup alerts. Run the staging smoke below before any production authorization.

## 6. Production-like staging smoke test

Use a separate Compose project, database, node/wallet, RPC cookie, API token and webhook secret. Follow [production-smoke-test.md](production-smoke-test.md). It must use the release candidate node/electrs images and BTCPay v2.4.4, but set `BTCX_NETWORK=regtest`, `BTCX_WALLET_NETWORK=regtest`, Electrs network `regtest`, and keep `BTCX_ALLOW_MAINNET=false`. Verify an actual regtest invoice/payment, listener discovery, confirmations, settlement and signed XBoard webhook. Never use mainnet, real funds, a production store or production XBoard in this rehearsal.

The repository's previous staging acceptance records a real 61.7 BTCX regtest payment, six confirmations and XBoard order paid. That historical result is not yet a production-like smoke against the new production Dockerfiles/final generator stack. Record a new run against immutable candidate images before closing this gate.

## 7. Non-production stop conditions

Do not accept production BTCX orders while any of these remain unresolved: production image registry digests, source/build attestations, upstream PoCX/bindex/electrs compatibility approval, wallet custody and recovery rehearsal, Phoenix device E2E decision, XBoard secret-file E2E on final staging, TLS/secret-manager/backup recovery drills, and named operations/security/finance approvals. No automatic mainnet deploy, wallet creation, XBoard production connection, or BTCX funds transfer is part of this packaging.
