#!/usr/bin/env bash
set -euo pipefail
rpc_port="${BTCX_RPC_PORT:-8332}"
if [[ "${BTCX_NETWORK:-main}" == "regtest" ]]; then rpc_port="${BTCX_RPC_PORT:-18443}"; fi
if [[ "${BTCX_NETWORK:-main}" == "test" ]]; then rpc_port="${BTCX_RPC_PORT:-48332}"; fi
bitcoin-cli -datadir=/data -rpcport="$rpc_port" -rpccookiefile=/run/btcx-rpc/.cookie getblockchaininfo >/dev/null
curl --fail --silent --show-error --max-time 5 \
  "http://127.0.0.1:${rpc_port}/rest/chaininfo.json" >/dev/null
