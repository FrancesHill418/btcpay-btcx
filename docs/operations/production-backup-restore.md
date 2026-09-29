# BTCX production backup and disaster recovery runbook

**Status:** standardized procedure; production wallet backup/restore and database restore have not been rehearsed. This document does not authorize production access. Never place secrets, wallet backups, seeds, or private keys in Git or a terminal transcript.

## Backup set and cadence

Create a coordinated backup set before plugin installation/upgrades and on the approved operations schedule:

- XBoard database and application/plugin configuration. Use the database engine, account, and backup tool from that XBoard deployment; this project does not assume whether it is MySQL, MariaDB, or PostgreSQL.
- BTCPay PostgreSQL database(s), BTCPay persistent datadir/server key/configuration, and installed plugin package/version.
- Bitcoin-PoCX wallet backup from the exact deployed node version. Keep encrypted copies offline/immutable in two access-controlled locations; keep decryption recovery under dual control. Record wallet name/network and encrypted artifact checksum only, never a seed/private key.
- Sanitized deployment manifest: Compose, non-secret effective configuration, image registry digests/platforms, source commits, patch hashes, `production-version-matrix.md`, plugin `.btcpay` checksum, SBOM and provenance references.
- Secret-manager recovery metadata and ACL procedure, never raw API key, HMAC, DB password, RPC cookie, TLS private key, or secret file content.
- Bitcoin-PoCX chain data and electrs index may be snapshotted to reduce recovery time; they are rebuildable and do not replace wallet-key backup.

## BTCPay PostgreSQL backup

Use the actual production compose project and verified database name. The BTCX production compose uses `btcpayserver${NBITCOIN_NETWORK}` and stores the PostgreSQL password in a Docker secret file. Stream the dump directly into the approved encryption tool/destination; do not print it or pass a password on the command line. Example for the repository's compose service (replace the output path/recipient through the operator's approved process):

```sh
umask 077
docker compose -f docker-compose.yml exec -T postgres \
  pg_dump -Fc -U postgres -d "btcpayserver${NBITCOIN_NETWORK}" \
  | age -r "$BACKUP_AGE_RECIPIENT" > "$BACKUP_DIR/btcpay-postgres.dump.age"
test -s "$BACKUP_DIR/btcpay-postgres.dump.age"
sha256sum "$BACKUP_DIR/btcpay-postgres.dump.age" > "$BACKUP_DIR/btcpay-postgres.dump.age.sha256"
```

Confirm the in-container DB role/authentication against the approved final compose before adopting the command. Never set `PGPASSWORD` to a literal in a command. For XBoard, run the matching engine's consistent online backup procedure, encrypt immediately, then verify the encrypted file and hash. If XBoard and BTCPay cannot be transactionally backed up together, record timestamps and reconcile their in-flight invoice/order IDs during restore.

## Configuration and wallet backup

1. Quiesce configuration changes and BTCX wallet metadata changes for the backup window; record source tag/commit, image digests, XBoard target, network/genesis, database names, wallet label, and UTC recovery point.
2. Use Bitcoin-PoCX's supported wallet backup RPC for the exact node release. Write only to a restricted backup mount accessible by the node account; encrypt the resulting wallet artifact before moving it off the host. Do not emit RPC responses containing key material, print file contents, or use shell tracing.
3. Save BTCPay `/datadir` and plugin package/checksum at a point coordinated with PostgreSQL. Keep the actual secrets in the secret manager; record only secret identifiers/version references.
4. Save XBoard configuration and its standalone `BtcpayBtcx` source commit/package separately from BTCPay's original `BTCPay` provider.
5. Save sanitized Compose/config, version matrix, patch hashes, image references, SBOM/provenance checksums and a backup inventory. Exclude `.env`, secret files, Docker auth, database dumps in plaintext, node wallet files and chain data from Git and ordinary logs.
6. Verify non-empty encrypted files, hashes, destination ACLs, retention and second-site/offline copies. A successful backup command without restore evidence is not completion.

## Restore order

Restore only into an isolated recovery environment with public ingress, outgoing webhooks, customer fulfillment, and fund movement disabled:

1. Retrieve the signed release manifest, exact images/plugin artifacts, secret-manager recovery authorization, and backup inventory. Verify hashes/signatures before use.
2. Restore BTCPay PostgreSQL and matching BTCPay datadir/server identity to a consistent recovery point. Verify stores, invoices, BTCX quote snapshots and in-flight payment IDs.
3. Start the exact Bitcoin-PoCX build on the recorded network, verify genesis/chain identity, and restore/load the encrypted wallet using that release's supported wallet recovery procedure. Compare protected wallet labels/address metadata without displaying keys.
4. If chainstate was not restored, fully re-sync the node. Rebuild electrs from an empty derived index using the exact compatible PoCX REST patch set and pinned bindex/electrs images. Verify node/index heights and sampled invoice-script history.
5. Restore XBoard database/configuration to its coordinated recovery point. Verify the original `BTCPay` provider still loads and the standalone `BTCPayBTCX` binding/delivery tables match BTCPay's in-flight invoices.
6. Reconcile each in-flight invoice/order/payment/event ID against canonical node state. Do not mark orders paid by SQL or synthetic webhook. Keep fulfillment paused until all discrepancies are resolved.
7. Run isolated non-mainnet healthchecks and a disposable payment/replay test, validate webhook HMAC and idempotency, then obtain dual operator approval before separately scheduling service restoration.

The BtcpayBtcx migration is separately owned. Disabling the plugin preserves its tables; do not run migration `down()` as normal rollback because it drops its invoice binding and webhook ledgers. Never delete volumes, reset the wallet, or restore staging data over production as a shortcut.

## Restore checklist

- [ ] Encrypted BTCPay database dump exists, checksum verifies, and test restore completed.
- [ ] Encrypted XBoard database dump exists, checksum verifies, and test restore completed.
- [ ] BTCPay datadir/config and exact BTCX plugin `.btcpay` artifact are available at matching recovery point.
- [ ] Bitcoin-PoCX encrypted wallet backup exists in two protected locations; exact-version restore completed without displaying key material.
- [ ] Node network/genesis and restored wallet labels match protected recovery metadata.
- [ ] electrs/bindex rebuilt or restored from the exact pinned build; node and index heights agree.
- [ ] Version matrix, image digests, patch hashes, SBOM, provenance and configuration manifest are available and verified.
- [ ] XBoard original `BTCPay` and independent `BTCPayBTCX` coexist; invoice/order/delivery IDs reconcile.
- [ ] No RPC, Electrum, database or secret file is publicly exposed; all secret references resolve without printing values.
- [ ] HTTPS, health checks, monitoring, backup age alerts, reboot recovery, callback idempotency, and restore duration are recorded.
- [ ] Incident/recovery owner and a second approver sign the evidence before production restoration.
