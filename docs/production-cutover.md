# Production cutover plan

**Cutover status: BLOCKED / NOT AUTHORIZED.** This is a future change plan. No production deployment, mainnet connection, real-fund transaction, or production XBoard integration is part of this release preparation.

## Go/no-go prerequisites

Do not schedule cutover until all items below have named owners and review evidence:

1. Current mainnet guard is replaced only by an intentional, reviewed plugin release; the present build must not be configured around its explicit refusal.
2. Phoenix real-device acceptance is complete or an accountable release authority has explicitly dispositioned the remaining gate.
3. PoCX node, bindex and electrs supported versions work without unapproved development patches; node/indexer compatibility and recovery have been proven.
4. All production dependencies are immutably pinned, reproducibly built, scanned, attested and listed by source hash, OCI digest, package lock and output checksum. Current Dockerfiles use mutable tags and unpinned OS package installs.
5. Dedicated production wallet custody, RPC boundaries, encrypted backup, restore drill, rate governance and deep-reorg compensation are approved.
6. Separate production databases, service identities, Greenfield credentials, XBoard configuration, webhook HMAC secrets and TLS certificates are provisioned in the approved secret manager. None are shared with staging.
7. Production HTTPS ingress, firewall, monitoring, alerting, retention, incident response, restore and rollback have passed rehearsals.
8. Production Security, Operations, Finance/Accounting and Product owners sign the release manifest and cutover ticket.

## Preparation (non-production)

1. Build artifacts in reviewed CI from the exact source and dependency lock. Record plugin/XBoard patch checksum, SBOM, scan results and immutable image digests.
2. Rehearse installation, upgrade, webhook retries/duplicates, manual-rate update, invoice quote lock, expiry, under/overpayment handling, six-confirmation LowSpeed policy, reorg rollback, database restore, wallet restore and index rebuild on isolated non-mainnet systems.
3. Verify all service endpoints, DNS, certificates, firewall routes, secret file ownership and monitoring from the approved production change record. Do not copy the staging `.env`, volumes, database, wallet, RPC cookie, rate or credentials.
4. Record current and target artifacts, backups, rollback owner, decision authority, maintenance window, support coverage and stop conditions. Production credentials stay out of this repository and logs.

## Future cutover sequence

These steps may be executed only after a separate explicit production change authorization and closure of every gate:

1. Take verified, encrypted pre-change backups of BTCPay and XBoard databases/configuration; verify wallet backup retrieval and secret-manager recovery without exposing secret values.
2. Deploy the reviewed immutable artifacts to private production networks. Keep BTCX order checkout disabled while services start and health checks run.
3. Verify node network/genesis, wallet status, RPC isolation, indexer sync and sampled address-history agreement. Confirm there is no staging network, wallet, database or credential route.
4. Apply reviewed BTCPay store settings, BTCX-CHAIN enablement, manual BTCX/CNY rate with its approval/expiry record, and the six-confirmation LowSpeed fulfillment policy.
5. Configure production Greenfield scope, XBoard HTTPS callback, `InvoiceSettled` subscription, HMAC verification, TLS expiry monitoring, retry queue and duplicate idempotency. Test with non-value-bearing health checks and signed fixtures only while order fulfillment remains disabled.
6. Enable a limited monitored cohort only after a further named go/no-go. Observe a real authorized customer payment only under the separately approved production transaction policy; this document authorizes none.
7. Monitor payment detection, canonical confirmations, invoice state, webhook status, XBoard order binding, rate freshness and reorg signals. Keep a named operator available through the observation window.

## Stop and recovery criteria

Stop new BTCX checkout immediately on wrong network/genesis, RPC/authentication anomaly, wallet/address mismatch, index lag/divergence, stale/unapproved rate, signature or invoice/order mismatch, repeated webhook failure, unexpected payment amount/state, backup/monitoring loss or suspected reorg. Preserve evidence, disable new BTCX invoices through the supported admin path, and reconcile in-flight payments before any rollback. Do not delete volumes or reset a live database/wallet as a rollback method.

Rollback must use the approved application artifact/configuration rollback and consistent database recovery plan. If schema/data changes are not backward compatible, keep checkout disabled and use the tested forward-recovery procedure. An invoice may already have a confirmed on-chain payment; application rollback does not reverse it. Reorg or already-fulfilled order recovery follows the approved customer/accounting compensation process.

## Current release decision

This repository's staging E2E is **PASS** on regtest. Production dependency pinning, mainnet support, production key custody, XBoard secret injection, Phoenix device acceptance, supported node/indexer release, and production DR/security approvals are not complete. Therefore this candidate is **NOT READY FOR PRODUCTION**. The RC tag labels preparation material only and does not authorize cutover.
