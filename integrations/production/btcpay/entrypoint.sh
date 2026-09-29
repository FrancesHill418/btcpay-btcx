#!/usr/bin/env bash
set -euo pipefail

secret_file=/run/secrets/btcx_postgres_password
if [[ ! -r "$secret_file" ]]; then
  echo "BTCPay PostgreSQL secret file is unavailable" >&2
  exit 1
fi
postgres_password="$(cat "$secret_file")"
if [[ -z "$postgres_password" || "$postgres_password" == *';'* ]]; then
  echo "BTCPay PostgreSQL secret file is invalid" >&2
  exit 1
fi
database="btcpayserver${NBITCOIN_NETWORK:-mainnet}"
export BTCPAY_POSTGRES="User ID=postgres;Host=postgres;Port=5432;Application Name=btcpayserver;Database=${database};Password=${postgres_password}"
unset postgres_password

exec /app/docker-entrypoint.sh "$@"
