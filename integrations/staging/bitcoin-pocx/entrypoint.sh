#!/usr/bin/env bash
set -euo pipefail

: "${BTCX_BACKEND_SUBNET:?Set BTCX_BACKEND_SUBNET to the isolated Docker backend subnet}"
mkdir -p /data /run/btcx-rpc
chmod 700 /run/btcx-rpc
sed "s|__RPC_ALLOWIP__|${BTCX_BACKEND_SUBNET}|g" \
  /etc/bitcoin/bitcoin.conf.template > /data/bitcoin.conf
chmod 600 /data/bitcoin.conf
if [[ ! -e /data/.btcxx-owner-initialized ]]; then
  chown -R bitcoin:bitcoin /data
  touch /data/.btcxx-owner-initialized
fi
chown bitcoin:bitcoin /data/bitcoin.conf /data/.btcxx-owner-initialized /run/btcx-rpc

exec gosu bitcoin /usr/local/bin/bitcoind \
  -datadir=/data \
  -conf=/data/bitcoin.conf \
  -printtoconsole
