#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose="$root/docker-compose.yml"
env_file="${1:-$root/.env}"

if [[ ! -r "$env_file" ]]; then
  echo "Protected deployment env file is missing or unreadable." >&2
  exit 2
fi

# Parse only safe key/value controls; never print or source the environment file.
allow_mainnet="$(awk -F= '$1 == "BTCX_ALLOW_MAINNET" {gsub(/[[:space:]]/, "", $2); print $2}' "$env_file" | tail -n 1)"
if [[ "${allow_mainnet,,}" != "false" ]]; then
  echo "Preflight requires BTCX_ALLOW_MAINNET=false." >&2
  exit 2
fi

for key in BTCPAY_IMAGE BTCX_NODE_IMAGE BTCX_ELECTRS_IMAGE; do
  ref="$(awk -F= -v key="$key" '$1 == key {sub(/^[^=]*=/, ""); print}' "$env_file" | tail -n 1)"
  if [[ ! "$ref" =~ ^[^[:space:]@]+:[^[:space:]@]+@sha256:[a-f0-9]{64}$ ]]; then
    echo "$key must be a real image reference pinned by sha256 digest." >&2
    exit 2
  fi
done

docker compose --env-file "$env_file" -f "$compose" config --quiet
model="$(docker compose --env-file "$env_file" -f "$compose" config --format json)"
python3 -c '
import json, re, sys
model = json.load(sys.stdin)
services = model["services"]
required = {"postgres", "bitcoin-pocx", "electrs-btcx", "btcpayserver"}
assert required.issubset(services), "missing required production services"
for name in required:
    service = services[name]
    assert service.get("restart") == "unless-stopped", f"{name}: restart policy missing"
    assert service.get("healthcheck"), f"{name}: healthcheck missing"
    assert not service.get("ports"), f"{name}: host port publishing is prohibited"
    image = service.get("image", "")
    assert re.fullmatch(r"[^@]+@sha256:[0-9a-f]{64}", image), f"{name}: immutable image reference required"
assert services["btcpayserver"]["environment"].get("BTCX__Wallet__AllowMainnet") == "false", "mainnet wallet gate must remain disabled"
for name in ("postgres", "bitcoin-pocx", "electrs-btcx", "btcpayserver"):
    assert services[name].get("volumes"), f"{name}: persistent data/credential volume missing"
' <<<"$model"

echo "Production Compose preflight passed; no services were started."
