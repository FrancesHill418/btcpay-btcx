# Backup and recovery

This procedure is for isolated staging state and future recovery planning. Never use a production wallet or production credentials in staging. Current development acceptance restored a regtest wallet backup, including 9 labels and 5 transaction IDs; this validates the tested wallet RPC path only. It is not a production recovery certification.

## State inventory

Keep and back up these assets independently, using encrypted storage with access logging and an offline/immutable copy where appropriate:

| State | Examples | Recovery purpose |
|---|---|---|
| BTCX node wallet | Dedicated `btcx-receive` wallet backup and wallet metadata | Recover receive addresses/labels and wallet transaction knowledge |
| BTCX node chain data | Regtest chainstate and block data | Rebuild/replay node state; for production this would be a large, independently verified data set |
| electrs-btcx index | Index database and index configuration/version | Rebuildable address/script history; can be re-indexed from a compatible node, but recovery time depends on chain size |
| BTCPay database | BTCPay database backup | Stores, invoices, payment state, plugin settings and invoice quote snapshots |
| XBoard database | XBoard database backup | Orders and invoice/order bindings |
| Configuration and release metadata | Sanitized Compose/customization files, image digests, source commits, plugin checksum | Recreate services without copying secrets into source control |
| Secrets | RPC auth, Greenfield token, webhook HMAC key, TLS/private credentials | Back up only through the approved secret manager's protected recovery mechanism; never place in this repository |

Never treat a Docker volume as a backup. Do not place database dumps, Docker volumes, BTCX chain data, wallet files, private keys, seeds, API keys, or webhook secrets in this repository or a release archive.

## Staging wallet backup and restore drill

1. Confirm the node is the isolated regtest node and the active wallet is `btcx-receive`. Record the node/source revision and wallet network in the change record.
2. Use the node's supported wallet backup RPC (for Bitcoin-PoCX builds this is typically `backupwallet`) to write the backup to a dedicated protected backup location. Use a unique timestamped destination and restrictive filesystem permissions. Do not send the backup file to chat, logs, or Git.
3. Verify the backup file exists and record its checksum in the protected staging record (the checksum itself is not a secret). Keep wallet backup separate from publicly accessible BTCPay and electrs mounts.
4. For a restore drill, stop or isolate the test wallet service and restore into a fresh disposable regtest datadir/wallet name using the node's documented wallet recovery procedure. Never overwrite the only copy of a wallet or restore a test backup over another environment.
5. Load the restored wallet and verify expected labeled receive addresses, transaction IDs/history, balances against the regtest chain, and ability to reconcile a known test payment. Wallet metadata alone cannot recreate missing blockchain/indexer history; allow the node and electrs index to catch up and verify invoice reconciliation separately.
6. Record elapsed time, source/target revisions, recovered labels and transactions, discrepancies, and operator. Securely destroy temporary restore copies when no longer needed.

Use only disposable staging funds. Avoid exporting or handling raw private keys/seeds unless an approved recovery procedure specifically requires it; this development plugin does not provide a production key-custody design.

## Service and database recovery order

For a full staging recovery, restore into a newly isolated environment with network egress and host port exposure disabled by default:

1. Recreate pinned BTCX node and indexer versions/configuration, then restore or rebuild the regtest chain and electrs index. Apply the documented development compatibility patch only for staging and verify REST/electrs compatibility before starting payment reconciliation.
2. Restore the dedicated regtest wallet and verify its network, labels, addresses, and history.
3. Restore BTCPay and XBoard databases from mutually consistent backup times. Restore configuration from reviewed sanitized files and inject secrets from the secret manager.
4. Install the plugin artifact that matches the recorded source commit and checksum. Confirm BTCPay v2.4.4 and BTCX plugin settings, including the `regtest` wallet and `manualbtcx(BTCX_CNY)` rate rule.
5. Start services on the private staging Docker networks. Confirm node RPC, electrs, webhook TLS/signature, invoice state, and order binding. Reconcile existing test invoices before sending any new test payments.
6. Run a new exact-payment smoke transaction on regtest and verify detection, confirmations, invoice state, and one idempotent XBoard settlement. Document unresolved discrepancies; do not manually edit invoice/order state to conceal them.

If the Bitcoin-PoCX node or indexer cannot be restored to a known compatible version, stop and rebuild the disposable regtest environment from pinned sources. Do not silently switch to another chain or indexer.

## Recovery limitations

The prior staging wallet restore test recovered 9 labels and 5 transaction IDs. The electrs index is rebuildable only when a compatible node and complete source chain are available. BTCPay/XBoard database consistency and production-grade encrypted backup rotation have not been certified by that test. Phoenix real-device E2E remains pending, and mainnet wallet recovery is unsupported by this development build.
