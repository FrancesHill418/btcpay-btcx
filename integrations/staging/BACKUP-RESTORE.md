# Staging backup and restore

This guide covers the Compose staging volumes only. It is not a production disaster-recovery certification. Backups may contain wallet keys and payment/customer records: encrypt them, restrict access, keep an offline copy, and never commit them to Git or attach them to public logs/issues.

## Included state

| Compose volume | Contents | Recovery method |
|---|---|---|
| `postgres_data` | BTCPay PostgreSQL database, invoices and plugin settings | `pg_dump` / `pg_restore` |
| `btcpay_data` | BTCPay data/config/runtime state | stop service and archive volume |
| `btcx_node_data` | BTCX regtest chain and `btcx-receive` wallet | node `backupwallet`; chain data can be re-synced |
| `btcx_electrs_data` | bindex/electrs index state | archive or rebuild from compatible regtest chain |
| `btcx_rpc_cookie` | ephemeral RPC authentication cookie only | do not back up; node regenerates it on restart |
| `btcx_electrs_rpc` | generated restricted electrs RPC credential and `rpcauth` salt/hash | keep private; it is separate from the BTCPay wallet cookie and may be regenerated only together with the node's matching config |

Docker volumes are not backups. Keep XBoard database backups in the XBoard operator's separate recovery process, and make the XBoard/BTCPay database backup times consistent when preserving a complete order/invoice link.

## Create a staging backup

Set a protected backup destination outside this repository and ensure the disk is encrypted and access restricted. Do not use a path under this checkout.

```sh
export BTCX_BACKUP_DIR=/secure/encrypted/path/btcx-staging-$(date -u +%Y%m%dT%H%M%SZ)
mkdir -m 700 -p "$BTCX_BACKUP_DIR"
docker compose stop btcpay electrs-btcx wallet-init bitcoin-pocx
```

Dump PostgreSQL using the injected Compose secret without printing its value:

```sh
docker compose exec -T postgres sh -ec \
  'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; exec pg_dump -U btcpay -Fc btcpayserver' \
  > "$BTCX_BACKUP_DIR/btcpayserver.dump"
```

Create the wallet-level backup after restarting the node (wallet `backupwallet` requires the wallet RPC to be available):

```sh
docker compose up -d bitcoin-pocx wallet-init
docker compose exec bitcoin-pocx bitcoin-cli -regtest \
  -rpccookiefile=/run/btcx-rpc/.cookie -rpcwallet=btcx-receive \
  backupwallet /tmp/btcx-receive-wallet.dat
docker cp "$(docker compose ps -q bitcoin-pocx):/tmp/btcx-receive-wallet.dat" \
  "$BTCX_BACKUP_DIR/btcx-receive-wallet.dat"
docker compose exec bitcoin-pocx rm -f /tmp/btcx-receive-wallet.dat
```

Optionally stop the stack and archive the persistent data volumes using a trusted volume backup tool. For a consistent full data snapshot, stop BTCPay, electrs, and the node before snapshotting all relevant volumes. Keep the plugin/image source revision, Docker image IDs/digests, and checksums next to the encrypted backup. Do not archive or copy `btcx_rpc_cookie`; it is ephemeral authentication state. Treat `btcx_electrs_rpc` as a secret volume; it contains only electrs's whitelisted RPC identity and does not authorize wallet RPC methods.

Restart services after completing the backup:

```sh
docker compose up -d
```

## Restore into a fresh disposable stack

Never restore over the only copy or into another environment. Use a fresh project name and isolated regtest volumes. Recreate the pinned images first, and recreate staging secrets through `init-secrets.sh` (PostgreSQL secret must match the restored database role password; use a separately protected secret backup if retaining the original database).

1. Stop the new stack and restore the sanitized deployment configuration and matching plugin/node/indexer images.
2. Restore the PostgreSQL dump into the fresh `btcpayserver` database. For an empty target, start PostgreSQL only, then run `pg_restore --clean --if-exists --no-owner -U btcpay -d btcpayserver` from a one-off Postgres container with the dump mounted read-only.
3. Restore `btcx_node_data` from a consistent protected volume snapshot when used. If unavailable, the regtest chain can be rebuilt; restore the wallet backup into a fresh node datadir using the exact BTCX network and node version, then load `btcx-receive` and rescan/reconcile history as needed.
4. Rebuild or restore `btcx_electrs_data` only for the matching electrs/bindex revision. If uncertain, use an empty index volume and allow it to reindex from the compatible BTCX node.
5. Do not restore the old RPC-cookie volume. Start the node so it creates a new cookie, then start wallet init, electrs, and BTCPay.
6. Verify node network is `regtest`, the wallet addresses/history match the test record, electrs indexes the node, BTCPay invoices and plugin settings exist, and an exact disposable regtest payment settles. Verify the corresponding XBoard order/invoice binding separately.

The prior code-validation wallet restore recovered 9 labels and 5 transaction IDs in regtest. This Compose package's full multi-volume backup/restore and XBoard database consistency have not yet been exercised end to end.

## Teardown

`docker compose down` retains data. `docker compose down -v` permanently deletes the Compose volumes, including the database, wallet and blockchain data; run it only when deliberately destroying the staging environment after confirming backups or disposal approval.
