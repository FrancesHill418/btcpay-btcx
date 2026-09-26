# BTCX plugin security model (design audit)

This is a threat-oriented design baseline, not a completed security review. No code, deployed service, secrets, wallet or live RPC endpoint was available for inspection.

## Assets and trust boundaries

| Asset | Owner / boundary | Security requirement |
|---|---|---|
| XBoard order and entitlement | XBoard application/database | Bind immutable order UUID to BTCPay invoice ID; validate webhook and dedupe before fulfillment |
| Invoice amount/rate/expiry | BTCPay invoice plus plugin audit metadata | Lock rate at creation; use fixed point/integer base units; never let a later rate mutate due amount |
| BTCX receive derivation material | Plugin/wallet boundary | Prefer receive-only xpub/descriptor; protect derivation state; never log seed/private keys |
| Signing keys / wallet seed | Separate wallet subsystem | Not required for receiving; encrypt and back up separately; no production wallet in tests |
| Node RPC | Private BTCPay↔node network | Bind to loopback/private Docker network; strong auth where supported; never publish port or place behind public proxy/tunnel |
| Node REST used by bindex | Private node RPC network | electrs-btcx docs describe REST as unauthenticated on RPC port; expose only to trusted indexer on internal network |
| Indexer address/transaction history | Private indexer service | Authenticate/network isolate where supported; do not expose publicly; treat response as untrusted until canonical reconciliation |
| Plugin database | BTCPay data boundary, plugin-owned schema | Least privilege, durable transactions, backup/restore and migration testing; no raw secrets unless necessary |
| Webhook signing secret/API key | BTCPay↔XBoard boundary | TLS, minimum scopes, protected config/secret store, rotate and never log |

## Threats and controls

| Threat | Attack/failure path | Required control |
|---|---|---|
| Forged/fake payment | Malicious client invents tx or indexer response | Verify transaction/output against BTCX serialization and node; require canonical block inclusion for settled state |
| Duplicate/replayed observation | Notification replay, polling overlap, plugin restart | Unique `(network,txid,vout)` key; idempotent upsert and recomputation |
| Webhook replay/forgery | Attacker resends or forges invoice event | Validate HMAC/signature and endpoint identity; persist delivery/event ID; bind invoice/order; compare trusted invoice data; fulfill once |
| Amount/currency mismatch | Wrong currency, rounding, float or malicious metadata | Validate BTCX network/currency and integer base-unit output sum against locked invoice due; reject unsupported precision |
| Invoice substitution | User associates paid invoice with another order | Persist invoice ID and immutable order UUID mapping; compare callback and BTCPay retrieval result |
| Expired/late payment race | Payment seen after invoice expiry or event ordering race | Persist observation timestamp/height and invoice expiry; use BTCPay's current late-payment semantics; require explicit fulfillment policy |
| Reorg / double spend | Confirmed block orphaned or input conflicts | Store block anchors; recheck canonical chain and mempool spend state; reverse payment confirmation; define post-fulfillment compensation policy |
| Node/indexer outage or stale response | Missed notifications or stale tip | Durable cursor, overlapping rescan, health monitoring, node reconciliation and restart tests |
| RPC credential/secret leak | Logs, exception serialization, compose file, support bundle | Redact config and request headers; use secret injection; test logs/config artifacts; never commit wallet or credentials |
| Public RPC exposure | Port published, reverse-proxied or tunnelled | No host port mapping; internal network only; firewall deny; audit deployed compose and listening sockets |
| Wallet compromise | Hot signer can spend all deposits | Receive-only mode first; separate signer, constrained funds, independent encrypted backup and restore drill |
| Malicious plugin/update | Plugin runs in BTCPay process with broad privileges | Pin reviewed releases/dependencies; verify compatibility and package provenance; least access; upgrade in test environment first |

## Key handling boundary

For invoice acceptance, the preferred design is a BTCX-compatible public derivation descriptor/xpub held by the receive service, with signing material isolated. Whether BTCPay's current store/wallet model can represent BTCX descriptors must be verified before selecting that option. If a node wallet is necessary for address derivation, restrict it to receive operations and do not make its availability a prerequisite for tracking already-issued invoices. Withdrawals remain a separate later feature.

## Operational gates before production

- Testnet/regtest only in automated tests; assert configured network and fail closed if mainnet is selected.
- RPC/indexer services private; no public DNS, reverse proxy or tunnel routes.
- Secret scanning of repository and CI artifacts; structured-log redaction tests.
- Backup/restore of plugin database and wallet derivation state; verify invoice mappings survive recovery.
- Restart, replay, reorg and compromised/stale-indexer tests before production.
- Security review of plugin dependencies, API scopes, database migration and webhook authentication.
- Mainnet dry run only after full regtest suite and backup/recovery rehearsal.
