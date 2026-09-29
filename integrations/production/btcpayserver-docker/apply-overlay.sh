#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
target="${1:?Usage: apply-overlay.sh /path/to/btcpayserver-docker-checkout}"
expected="9c8fe127850d079405dbb2db0748611548dc1a42"
actual="$(git -C "$target" rev-parse HEAD)"
if [[ "$actual" != "$expected" ]]; then
  echo "Expected btcpayserver-docker $expected, got $actual" >&2
  exit 2
fi
install -m 0644 "$repo_root/integrations/production/btcpayserver-docker/docker-compose-generator/docker-fragments/btcx.yml" \
  "$target/docker-compose-generator/docker-fragments/btcx.yml"
install -m 0644 "$repo_root/integrations/production/btcpayserver-docker/crypto-definitions.json" \
  "$target/docker-compose-generator/crypto-definitions.json"
if grep -q 'POSTGRES_HOST_AUTH_METHOD: trust' "$target/docker-compose-generator/docker-fragments/postgres.yml"; then
  git -C "$target" apply --check "$repo_root/integrations/production/btcpayserver-docker/postgres-auth.patch"
  git -C "$target" apply "$repo_root/integrations/production/btcpayserver-docker/postgres-auth.patch"
elif grep -q 'POSTGRES_HOST_AUTH_METHOD' "$target/docker-compose-generator/docker-fragments/postgres.yml"; then
  echo "Unexpected PostgreSQL host authentication setting" >&2
  exit 2
fi
if grep -Fq 'image: ${BTCPAY_IMAGE:-btcpayserver/btcpayserver:2.4.4}' "$target/docker-compose-generator/docker-fragments/btcpayserver.yml"; then
  git -C "$target" apply --check "$repo_root/integrations/production/btcpayserver-docker/btcpay-image-lock.patch"
  git -C "$target" apply "$repo_root/integrations/production/btcpayserver-docker/btcpay-image-lock.patch"
elif ! grep -Fq 'image: ${BTCPAY_IMAGE:?Set the BTCPay v2.4.4 derived image by immutable tag and digest}' "$target/docker-compose-generator/docker-fragments/btcpayserver.yml"; then
  echo "Unexpected BTCPay image selector; refusing to patch" >&2
  exit 2
fi
echo "BTCX generator overlay installed at pinned upstream commit $expected. Commit these changes in the deployment fork before production."
