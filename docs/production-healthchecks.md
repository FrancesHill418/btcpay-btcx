# Production service health checks

The BTCX deployment fragment defines application-aware checks. Review actual results with `docker compose ps`; a healthy status does not prove complete node synchronization or a successful payment.

| Service | Runtime check | What it proves | Additional operational check |
|---|---|---|---|
| PostgreSQL | `pg_isready` against the BTCPay mainnet database | PostgreSQL accepts connections | Confirm BTCPay completed startup/migrations; monitor backups/storage. |
| Bitcoin-PoCX | Authenticated `getblockchaininfo` through the mounted RPC cookie, then HTTP GET `/rest/chaininfo.json` | RPC and the required REST endpoint are responding | Check expected network/genesis, `txindex=1`, sync lag, peer count, disk and reorg alerts. REST/RPC share 8332 internally; neither is published. |
| electrs-btcx | Electrum `server.version` request/valid response | Electrum protocol endpoint accepts requests | Compare `blockchain.headers.subscribe` height to the node; query representative script history; health alone does not prove index catch-up. |
| BTCPay | HTTP request to `/api/v1/health`, requiring HTTP 200 | BTCPay's HTTP health endpoint responds | Verify BTCX plugin loaded, Greenfield access, node/wallet RPC, Electrum and payment listener logs/metrics. |

## Network rules

Only BTCPay's public HTTPS endpoint should be inbound Internet reachable, through the selected TLS proxy. Bitcoin-PoCX needs outbound peer access for sync and ongoing chain updates, so the node joins a dedicated ordinary Docker bridge network for P2P egress. Its RPC/REST (8332), Electrum (50001), and PostgreSQL (5432) have no host port mappings. RPC/REST and PostgreSQL remain on internal networks; no P2P port is published to the host. Do not add RPC/debug exposure fragments in production.

## Manual diagnostic checks

Run only from the restricted host and avoid printing cookie contents:

```sh
docker compose exec bitcoin-pocx bitcoin-cli -datadir=/data getblockchaininfo
docker compose exec bitcoin-pocx curl --fail --silent http://127.0.0.1:8332/rest/chaininfo.json
docker compose exec electrs-btcx python3 /usr/local/bin/electrs-healthcheck.py 127.0.0.1 50001
docker compose exec btcpayserver bash -c 'exec 3<>/dev/tcp/127.0.0.1/49392; printf "GET /api/v1/health HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3; head -n 1 <&3'
docker compose exec postgres pg_isready
```

For regtest generated-stack checks, adjust RPC/Electrum ports from the deployment environment. Never paste unrestricted `docker compose config`, environment dumps, or secret-volume contents into logs/tickets.
