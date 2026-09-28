#!/usr/bin/env bash
set -euo pipefail

wallet_name=btcx-receive
rpc_args=(-regtest -rpcconnect=bitcoin-pocx -rpcport=18443 -rpccookiefile=/run/btcx-rpc/.cookie)
for _ in $(seq 1 60); do
  if bitcoin-cli "${rpc_args[@]}" getblockchaininfo >/dev/null 2>&1; then
    break
  fi
  sleep 2
done
bitcoin-cli "${rpc_args[@]}" getblockchaininfo >/dev/null

wallets="$(bitcoin-cli "${rpc_args[@]}" listwallets)"
if grep -Fq "\"${wallet_name}\"" <<<"$wallets"; then
  exit 0
fi

if bitcoin-cli "${rpc_args[@]}" loadwallet "$wallet_name" >/dev/null 2>&1; then
  exit 0
fi
bitcoin-cli "${rpc_args[@]}" createwallet "$wallet_name" >/dev/null
