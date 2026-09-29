# Staging healthchecks and acceptance

The Compose health state indicates each service's local readiness. It does not by itself prove full synchronization, a payment, a BTCPay invoice settlement, or XBoard webhook completion.

## Compose checks

```sh
docker compose ps
docker compose logs --tail=100 postgres bitcoin-pocx wallet-init electrs-btcx btcpay
```

Expected health gates:

| Service | Check | Meaning / limitation |
|---|---|---|
| `postgres` | `pg_isready` against `btcpayserver` | PostgreSQL accepts connections; does not verify BTCPay migrations |
| `bitcoin-pocx` | authenticated `getblockchaininfo` over local cookie RPC | Node RPC responds; not a sync-height or wallet check |
| `wallet-init` | one-shot `loadwallet` or `createwallet` RPC | `btcx-receive` is loaded; completed container is expected to exit |
| `electrs-btcx` | Electrum `server.version` JSON-RPC | Electrum protocol listener responds; not proof initial indexing has completed |
| `btcpay` | HTTP 200 from `/api/v1/health` | BTCPay HTTP endpoint responds; Greenfield readiness and BTCX path still require checks below |

The node `/rest` APIs and RPC use the same internal listener. RPC, REST, P2P and Electrum are not published to the host. `btcx-staging-backend` is an internal Docker network. PostgreSQL has no host port mapping.

BTCPay and `wallet-init` use the node cookie. electrs receives a separate `rpcauth` credential whose JSON-RPC methods are limited to its pinned indexer/broadcast calls; it does not receive the wallet cookie or wallet RPC methods. `rpcwhitelistdefault=0` preserves the cookie-authenticated BTCPay identity's full wallet RPC access. The listener scans BTCPay-monitored active invoices, including invoices with an in-flight payment after expiry, plus settlements recorded in the default 72-hour reorg safety window. Older settled BTCX invoices leave the polling set; restart recovery reloads recent settlement events from BTCPay's invoice event log.

In the current Compose topology, `electrs-btcx` sets `network_mode: service:bitcoin-pocx` because pinned bindex-btcx calls node REST at localhost. Therefore its `127.0.0.1:60401` healthcheck is the Electrum listener in the shared network namespace, and `127.0.0.1:18443` from electrs reaches Bitcoin-PoCX. The Compose DNS name `bitcoin-pocx` resolves to the same node from BTCPay and was also verified from electrs. Do not replace these localhost targets with a different service name unless the network topology and bindex REST target are changed together.

## Chain, wallet, and index readiness

```sh
docker compose exec bitcoin-pocx bitcoin-cli -regtest -rpccookiefile=/run/btcx-rpc/.cookie getblockchaininfo
docker compose exec bitcoin-pocx bitcoin-cli -regtest -rpccookiefile=/run/btcx-rpc/.cookie listwallets
docker compose exec electrs-btcx python3 /usr/local/bin/electrs-healthcheck.py 127.0.0.1 60401
docker compose exec btcpay curl --fail --silent http://127.0.0.1:49392/api/v1/health
```

For index catch-up, compare the node's `blocks`/`headers` with an Electrum `blockchain.headers.subscribe` response. Check server logs for index completion and ensure an address query returns normally. The healthcheck intentionally does not claim the index is synchronized merely because the listener opened.

The pinned PoCX source is based on Bitcoin Core v30.2.1 and lacks the `/rest/blockpart` route required by pinned bindex-btcx. The staging Dockerfile applies `integrations/electrs-btcx/bitcoin-pocx-v30-blockpart-compat.patch`; this is a development bridge only, and startup/index failures must be treated as failures rather than bypassed.

## Payment path smoke

1. In BTCPay, complete the first local administrator signup, create a CNY store, enable BTCX at `/server/btcx`, and set a manual rate. Confirm the store's `BTCX_CNY` rule points at `manualbtcx(BTCX_CNY)`.
2. Create an invoice that includes `BTCX-CHAIN`. Record only non-secret test identifiers and verify CNY amount, BTCX amount, address, URI, and immutable rate/timestamp snapshot.
3. Send the exact BTCX amount to the invoice address from the regtest wallet. Do not use a mainnet address or external mainnet wallet. Wait until Electrs reports history and BTCPay records the payment.
4. Generate additional regtest blocks if needed for the configured speed policy. Verify the invoice state progresses to settled and remains linked to the expected transaction and amount.
5. For XBoard, use the pinned patched development checkout, create a CNY order through Greenfield, and verify `metadata.orderId`, selected method `BTCX-CHAIN`, webhook signature, `InvoiceSettled` event and paid order state. The XBoard service is external to this Compose package; a previous live XBoard E2E does not replace verification against this Compose deployment.

For a wallet RPC send, substitute the invoice destination and exact invoice BTCX amount in BTCX units:

```sh
docker compose exec bitcoin-pocx bitcoin-cli -regtest \
  -rpccookiefile=/run/btcx-rpc/.cookie -rpcwallet=btcx-receive \
  sendtoaddress '<invoice-rpocx-address>' '<exact-btcx-amount>'
```

Record the returned transaction ID, then query it with `getrawtransaction <txid> true` after it is mined (or use `gettransaction <txid>` in the sending wallet). Confirm the Electrum history and BTCPay invoice payment agree on transaction ID and amount.

Do not send large blocks of logs to public systems; redact any environment/configuration output first. The node cookie and PostgreSQL password must never be printed.
