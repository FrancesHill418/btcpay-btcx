# BTCX wallet backup and recovery

This procedure defines the controls required for production preparation. It is not authorization to create a mainnet wallet or connect to mainnet. The current plugin rejects mainnet and uses spend-capable node wallet RPC; production key custody and restore remain unapproved gates.

## Environment isolation

Production and staging must have different:

* BTCX wallet/datadir and wallet backup set;
* RPC cookie or RPC username/password, node ACL and service identity;
* PostgreSQL and BTCPay/XBoard databases;
* Greenfield API key and XBoard API credentials;
* webhook HMAC secret, TLS keys, secret-manager namespace, hosts, networks and backup targets.

Never restore a staging wallet or database into production. Tag every backup with environment, network, wallet name, node version/source revision, creation time and operator in protected metadata; do not place sensitive metadata or wallet files in Git.

## Before production wallet creation

Close the mainnet support, custody and security gates in [production-deployment.md](production-deployment.md). Obtain an approved wallet creation ceremony/runbook for the exact Bitcoin-PoCX release and custody design. The current plugin calls wallet RPC `getaddressesbylabel` and `getnewaddress` and stores invoice address/script metadata in BTCPay. A node RPC cookie is powerful wallet authority even though the plugin does not implement withdrawals. Do not assume the wallet is watch-only: staging `getwalletinfo` reported `private_keys_enabled=true`.

During the future approved ceremony:

1. Verify the host, binary checksum, source revision, network, chain genesis and RPC endpoint from an independent change record before wallet creation. Use dedicated production infrastructure, never this staging Compose project.
2. Create/load one dedicated receiving wallet using the release-specific Bitcoin-PoCX wallet procedure. Record only wallet name, network, derivation/key-custody type, creation date and public verification data in the access-controlled operations record. Never record seed/private key content in tickets, docs, `.env`, command output or Git.
3. Verify wallet status, network and a newly allocated receive address; confirm address network prefix and script. Do not fund it as part of this release preparation task.
4. Restrict wallet administration and signing to named operators with MFA/dual control. Separate receive-address service access from spending approval where the approved architecture permits.
5. Make an encrypted wallet backup immediately after creation and after approved wallet/key metadata changes. Encrypt before transfer, verify integrity/checksum, store copies in geographically separated access-controlled offline/immutable locations, and test retrieval under dual control.

## Backup sets

Keep coordinated recovery points for:

| Item | Treatment |
|---|---|
| BTCX wallet backup and wallet metadata | Encrypted, tightly access-controlled, offline/immutable copies; never an unencrypted Git or container artifact |
| BTCPay PostgreSQL database | Consistent encrypted database dump/snapshot and restore record |
| XBoard database | Consistent encrypted backup with the matching BTCPay invoice/order linkage window |
| BTCPay persistent configuration/data | Protected backup consistent with database recovery point |
| Bitcoin-PoCX chain data | Optional performance snapshot; independently re-syncable from an authenticated compatible node/source |
| bindex/electrs data | Rebuildable derived state; preserve exact indexer version/configuration and chain compatibility evidence |
| Deployment manifest and plugin artifact | Sanitized settings, exact source/image digests, SBOM and checksums; no secret values |
| Secrets and TLS keys | Recover through secret manager's protected backup and break-glass procedure; never bundle in application/database backup |

Use application-aware PostgreSQL backups (for example `pg_dump -Fc`) or a supported consistent snapshot. Quiesce writers when taking cross-service consistency snapshots. Encrypt in transit and at rest, restrict restore privileges, validate backups on a schedule, and define retention/deletion to match accounting and privacy requirements. Docker volumes alone are not backups.

## Restore rehearsal

Restore only into an isolated, access-controlled recovery environment using the same approved production release manifest. Do not overwrite the only copy of a live wallet or database.

1. Provision an isolated recovery network with no public ingress. Load exact approved node/indexer/application artifacts by immutable digest and restore configuration with fresh/recovered secrets from the secret manager.
2. Restore the PostgreSQL and XBoard databases to mutually consistent recovery points. Keep webhook delivery/fulfillment paused during initial reconciliation.
3. Restore the wallet using the exact node version's documented wallet import/load mechanism into a protected datadir. Never paste private material into a shell command. Verify wallet name/network, expected labels/addresses, transaction history and balance against the independently synced canonical chain.
4. If node chain data is unavailable, resync from a verified source. Rebuild electrs/bindex with empty derived index state if version/consistency is uncertain; see [production-deployment.md](production-deployment.md) and the staging procedure for architecture details. Wait until node/indexer heights and sampled address history agree.
5. Verify BTCPay stores and invoice quote snapshots, XBoard order/invoice mappings, provider configuration and pending webhook delivery records. Keep outbound callbacks disabled until duplicate/idempotency and payment-state reconciliation are reviewed.
6. Reconcile all in-flight invoices against canonical node transactions and stored scripts. Escalate any missing/changed payment or already-fulfilled order; do not edit database rows to force a paid state.
7. Record backup IDs, artifact digests, restore duration, recovered wallet labels/transactions, invoice/order counts, discrepancies and approving operators. Destroy temporary recovery copies through the approved secure disposal process.

The development regtest wallet restore recovered 9 labels and 5 transaction IDs. That result validates only the tested development RPC backup path; it does not certify production encryption, custody, outage recovery, scale or secret recovery.

## Credential and wallet rotation

Rotate RPC access separately from wallet keys. For cookie authentication, schedule the node's supported restart/credential regeneration, keep RPC restricted to the private interface, ensure the new cookie is delivered to the BTCPay container through its read-only secret mount, verify RPC access and log redaction, then revoke/delete the old credential copy through the secret manager. Do not copy a cookie into an environment value or image.

For a receiving-wallet/key rotation, do not overwrite the current wallet backup. Pause new BTCX invoices, make and verify an encrypted backup of the old wallet, create a new dedicated wallet through the approved custody ceremony, verify its network/genesis and receiving address, then update BTCPay's wallet name using controlled secret/config delivery. Reconcile all old invoices until expiry/settlement; retain the old wallet loaded and protected for history/recovery until there are no invoice or accounting dependencies. Any later movement of funds from old to new wallet requires a separately approved transaction procedure and dual authorization. This release preparation does not initiate a rotation or transaction.

## Loss or compromise response

If a wallet backup, signing device, RPC credential, database or webhook secret is lost or exposed, page the designated security incident owner. Restrict the affected identity/network path, preserve audit evidence, revoke/rotate credentials through the approved sequence, assess whether receive addresses or funds are at risk, and reconcile all outstanding invoices. Do not email backup files or place them in an incident ticket. If signing keys may be compromised, follow the separately approved customer/treasury recovery plan; this repository does not define a safe key migration for production.
