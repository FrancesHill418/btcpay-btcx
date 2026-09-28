#!/usr/bin/env bash
set -euo pipefail

postgres_password="$(cat /run/secrets/postgres_password)"
export BTCPAY_POSTGRES="Host=postgres;Port=5432;Database=btcpayserver;Username=btcpay;Password=${postgres_password}"
unset postgres_password

exec /app/docker-entrypoint.sh \
  --nodefaultchain \
  "--disable-registration=${BTCPAY_DISABLE_REGISTRATION:-false}"
