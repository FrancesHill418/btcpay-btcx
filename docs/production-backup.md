# BTCX production backup and recovery

**Status:** required operating procedure; production backup and restore have not been rehearsed. Do not use staging material for production recovery.

## Critical and rebuildable data

| Class | Data | Recovery expectation |
|---|---|---|
| **CRITICAL** | Bitcoin-PoCX wallet data and encrypted wallet backup | Contains/controls receiving wallet keys and labels. Back up after wallet creation and approved changes; recovery must use the exact compatible node release and be verified offline/in an isolated environment. |
| **CRITICAL** | PostgreSQL data | Contains BTCPay stores, invoices, payment states, plugin settings and webhook/configuration records. Use consistent encrypted backup and test restore. |
| **CRITICAL** | BTCPay/plugin configuration and persistent datadir | Includes server key/configuration and plugin settings. Coordinate recovery with the PostgreSQL recovery point. Preserve plugin artifact/version and sanitized deployment manifest. |
| **CRITICAL** | Secret manager records | PostgreSQL password, RPC cookie delivery procedure, XBoard Greenfield token, XBoard webhook HMAC, TLS/private keys and recovery credentials. Back up/escrow through the secret manager's protected recovery; never package raw secret values with application backups. |
| **CRITICAL** | XBoard database and configuration (if used) | Keep order/invoice binding and webhook idempotency state at a recovery point compatible with BTCPay. |
| **REBUILDABLE** | Bitcoin-PoCX blockchain/chainstate synchronization data | Can be re-synced from the selected verified node release and network. A snapshot may reduce recovery time but is not the sole backup of wallet keys. |
| **REBUILDABLE** | electrs-btcx/bindex index database | Derived from node chain and scripts. Rebuild using the exact approved electrs/bindex binary, REST-compatible node and configuration. Reindex time/storage must be measured. |

## Backup procedure

1. Record the intended recovery point, deployment release/source commits, image digests, node network/genesis, wallet name, and plugin version in the access-controlled change record. Do not record seeds/private keys or RPC cookie values.
2. Use the approved Bitcoin-PoCX wallet backup method for the exact deployed version. Pause wallet metadata changes while producing a consistent artifact. Encrypt the backup before it leaves the node host, verify its checksum, and transfer it to two geographically separated access-controlled offline/immutable locations. Store decryption recovery under dual control.
3. Use BTCPay's supported backup workflow or a consistent PostgreSQL dump/snapshot. For a logical PostgreSQL backup, use the deployment's actual database/user and `pg_dump -Fc`; do not put a password on the command line. Quiesce or coordinate writes if the BTCPay datadir is backed up separately. Record database and datadir recovery timestamps together.
4. Back up the XBoard database/configuration at a recovery point coordinated with BTCPay. Preserve invoice/order IDs and event idempotency state.
5. Back up TLS/configuration metadata, sanitized Compose inputs, plugin `.btcpay` package and checksum, image lock, patch hashes, and secret-manager recovery instructions. Never include secret files, `.env` values, Docker credential stores or raw wallet material in this bundle.
6. Encrypt transport and storage, restrict access and retention, and alert on backup age/failure. A Docker volume, filesystem snapshot without consistency controls, or the node's live datadir alone is not a backup.

## Restore procedure

Restore into an isolated recovery environment with no public ingress and outbound webhooks/fulfillment paused:

1. Recover approved image/source manifests and secret-manager access. Install the exact BTCPay/plugin, Bitcoin-PoCX, electrs/bindex builds and configurations; never use staging secrets or volumes.
2. Restore PostgreSQL and BTCPay/plugin datadir to mutually compatible recovery points. Verify BTCPay server identity, stores, BTCX plugin settings, invoices and quote snapshots.
3. Restore the encrypted wallet artifact using the exact Bitcoin-PoCX wallet restore procedure and compatible node version. Keep spending/fund movement disabled. Verify loaded wallet name, network/genesis, labels and derived addresses against protected expected metadata without exposing keys.
4. If blockchain data was not restored, allow the node to fully synchronize from a verified source. Rebuild electrs/bindex from a clean index when provenance/consistency is uncertain. Verify node/index heights and sample invoice script history before resuming listener operations.
5. If XBoard is used, restore its database/configuration to a compatible time. Reconcile all in-flight order/invoice/payment/webhook IDs with BTCPay and canonical chain state before enabling webhook delivery.
6. Run the non-mainnet recovery smoke test, record differences and recovered counts, obtain dual approval, and only then plan a separately authorized production service restoration.

Never edit PostgreSQL rows to force an invoice/order paid, replay a synthetic callback, delete an existing volume as a shortcut, or restore staging wallet/database data over production.

## Rebuild procedure

When only derived chain/index data is unavailable, keep the critical wallet/database/config backups intact. Provision a fresh node data volume using the exact immutable node image, restore/load the wallet through the approved process, synchronize the complete chain, then start the exact electrs-btcx image with an empty electrs data volume. Wait for index completion and compare sampled transaction/address results, node height, and Electrum height. Reconcile active BTCPay invoices before re-enabling payment acceptance. Estimate disk, bandwidth, and recovery time from a measured non-production rehearsal.

## Required restore rehearsal evidence

Before production authorization, record backup IDs/checksums, image digests, recovery duration, wallet label/address/transaction verification, database invoice/order counts, node/index height agreement, in-flight invoice reconciliation, discrepancies, and approving operators. A previous development regtest wallet restore is not production restore evidence.
