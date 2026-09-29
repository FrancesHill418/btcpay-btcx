# Production Compose candidate

This directory is separate from the root regtest Compose. The Compose file describes the BTCPay/PostgreSQL/Bitcoin-PoCX/electrs runtime only; XBoard remains independently installed and receives `integrations/xboard/BtcpayBtcx/` at `XBoard/plugins/BtcpayBtcx/`. It registers `BTCPayBTCX`; the original `BTCPay` provider stays unchanged.

This is a fail-closed template, not an immediately deployable production stack. It requires real reviewed digest-pinned image references, an operator-approved private subnet, an already-created ingress network, and a protected PostgreSQL password file. No production registry, domain, IP, secret manager, or infrastructure values are assumed here. The ingress proxy/tunnel is deliberately external because its provider and DNS/TLS mode were not supplied. It may reach BTCPay at `btcpayserver:49392` on the configured external ingress network. No host `ports:` are published by this Compose file; in particular node RPC/REST, electrs, and PostgreSQL are not public.

All named image references are required at deploy time. The candidate tags and registry digests are recorded in the release manifest only after real image publication. Never fill them with fabricated digests or use the staging image IDs. Pin the helper images used by the chosen ingress separately.

The mainnet chain environment is explicit, but `BTCX_ALLOW_MAINNET` defaults to `false`; BTCX address allocation/payment remains unavailable until a separately authorized later change. `docker compose config` does not start a container. Do not run `up` during release preparation.

## Safe checks

Copy this example only into the operator's protected deployment directory after real image refs and network values are assigned there:

```sh
docker compose --env-file .env -f docker-compose.yml config --quiet
./checks/preflight.sh
```

In this repository, with unresolved image/network placeholders, use the structural-only check:

```sh
docker compose --env-file .env.example -f docker-compose.yml config --no-interpolate --quiet
```

`--no-interpolate` is for syntax/model inspection only; it is not a deployable configuration. The operator must separately inspect the final interpolated Compose and verify every `image` is `name:tag@sha256:<real digest>` before deployment.

BTCX plugin installation is a separate controlled step: build and verify the `.btcpay` release artifact, install it on BTCPay v2.4.4, then install/enable the standalone XBoard plugin and its migrations. Keep `/run/secrets/btcpay-btcx-api-key` and `/run/secrets/btcpay-btcx-webhook-key` as separate read-only secret files in the XBoard service. Host copies should be mode `0600` and owned by the approved secret-delivery identity; verify the effective container account can read them without printing contents.

Refer to [production operations](../../docs/operations/production-backup-restore.md), [version matrix](../../docs/production-version-matrix.md), and [release manifest](../../docs/release-manifest-v0.1.0-rc3.md) before an independently approved deployment.
