#!/usr/bin/env bash
set -euo pipefail

: "${BTCX_RPC_ALLOWIP:?Set the private Compose subnet allowed to reach node RPC/REST}"
: "${BTCX_NETWORK:=main}"
case "$BTCX_NETWORK" in
  main) chain_config=''; rpc_port="${BTCX_RPC_PORT:-8332}"; p2p_port="${BTCX_P2P_PORT:-8333}" ;;
  regtest) chain_config='regtest=1'; rpc_port="${BTCX_RPC_PORT:-18443}"; p2p_port="${BTCX_P2P_PORT:-18444}" ;;
  *) echo "BTCX_NETWORK must be main or regtest" >&2; exit 2 ;;
esac
if [[ ! "$BTCX_RPC_ALLOWIP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}/[0-9]{1,2}$ ]]; then
  echo "BTCX_RPC_ALLOWIP must be a private IPv4 CIDR" >&2
  exit 2
fi
mkdir -p /data /run/btcx-rpc
chmod 700 /run/btcx-rpc
sed -e "s|__CHAIN_CONFIG__|${chain_config}|g" \
  -e "s|__RPC_PORT__|${rpc_port}|g" \
  -e "s|__P2P_PORT__|${p2p_port}|g" \
  -e "s|__RPC_ALLOWIP__|${BTCX_RPC_ALLOWIP}|g" \
  /etc/bitcoin/bitcoin.conf.template > /data/bitcoin.conf
chmod 600 /data/bitcoin.conf
if [[ ! -e /data/.btcxx-owner-initialized ]]; then
  chown -R bitcoin:bitcoin /data
  touch /data/.btcxx-owner-initialized
fi
chown bitcoin:bitcoin /data/bitcoin.conf /data/.btcxx-owner-initialized /run/btcx-rpc
exec gosu bitcoin /usr/local/bin/bitcoind -datadir=/data -conf=/data/bitcoin.conf
