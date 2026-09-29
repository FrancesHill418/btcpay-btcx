#!/usr/bin/env bash
set -euo pipefail

: "${BTCX_RPC_ALLOWIP:?Set the private Compose subnet allowed to reach node RPC/REST}"
: "${BTCX_NETWORK:=main}"
: "${BTCX_ELECTRS_RPC_DIR:=/run/btcx-electrs-rpc}"
case "$BTCX_NETWORK" in
  main) chain_config=''; rpc_port="${BTCX_RPC_PORT:-8332}"; p2p_port="${BTCX_P2P_PORT:-8333}" ;;
  regtest) chain_config='regtest=1'; rpc_port="${BTCX_RPC_PORT:-18443}"; p2p_port="${BTCX_P2P_PORT:-18444}" ;;
  *) echo "BTCX_NETWORK must be main or regtest" >&2; exit 2 ;;
esac
if [[ ! "$BTCX_RPC_ALLOWIP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}/[0-9]{1,2}$ ]]; then
  echo "BTCX_RPC_ALLOWIP must be a private IPv4 CIDR" >&2
  exit 2
fi
mkdir -p /data /run/btcx-rpc "$BTCX_ELECTRS_RPC_DIR"
chmod 700 /run/btcx-rpc
chmod 755 "$BTCX_ELECTRS_RPC_DIR"
electrs_cookie="$BTCX_ELECTRS_RPC_DIR/.cookie"
electrs_auth="$BTCX_ELECTRS_RPC_DIR/.rpcauth"
if [[ ! -e "$electrs_cookie" && ! -e "$electrs_auth" ]]; then
  salt="$(openssl rand -hex 8)"
  password="$(openssl rand -hex 32)"
  password_hash="$(python3 -c 'import hashlib,hmac,sys; print(hmac.new(sys.argv[1].encode(), sys.argv[2].encode(), hashlib.sha256).hexdigest())' "$salt" "$password")"
  umask 077
  printf 'electrs:%s' "$password" > "$electrs_cookie.tmp"
  printf '%s$%s' "$salt" "$password_hash" > "$electrs_auth.tmp"
  mv "$electrs_cookie.tmp" "$electrs_cookie"
  mv "$electrs_auth.tmp" "$electrs_auth"
elif [[ ! -s "$electrs_cookie" || ! -s "$electrs_auth" ]]; then
  echo "electrs RPC credentials volume is incomplete; refusing to start with mismatched credentials" >&2
  exit 2
fi
if ! grep -Eq '^electrs:[a-f0-9]{64}$' "$electrs_cookie" || ! grep -Eq '^[a-f0-9]{16}\$[a-f0-9]{64}$' "$electrs_auth"; then
  echo "electrs RPC credentials volume has invalid content" >&2
  exit 2
fi
chmod 644 "$electrs_cookie" "$electrs_auth"
electrs_rpc_auth="$(cat "$electrs_auth")"
sed -e "s|__CHAIN_CONFIG__|${chain_config}|g" \
  -e "s|__RPC_PORT__|${rpc_port}|g" \
  -e "s|__P2P_PORT__|${p2p_port}|g" \
  -e "s|__RPC_ALLOWIP__|${BTCX_RPC_ALLOWIP}|g" \
  -e "s|__ELECTRS_RPC_AUTH__|${electrs_rpc_auth}|g" \
  /etc/bitcoin/bitcoin.conf.template > /data/bitcoin.conf
chmod 600 /data/bitcoin.conf
if [[ ! -e /data/.btcxx-owner-initialized ]]; then
  chown -R bitcoin:bitcoin /data
  touch /data/.btcxx-owner-initialized
fi
chown bitcoin:bitcoin /data/bitcoin.conf /data/.btcxx-owner-initialized /run/btcx-rpc
exec gosu bitcoin /usr/local/bin/bitcoind -datadir=/data -conf=/data/bitcoin.conf
