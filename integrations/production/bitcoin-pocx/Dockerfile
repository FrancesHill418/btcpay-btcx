# BTCX production candidate node image. Source revisions and the complete source
# patch set are pinned here; publish the resulting image and lock its registry
# manifest digest before production use.
ARG UBUNTU_IMAGE=ubuntu:24.04@sha256:008173c23f95b170204355c12626cb5a965d779a7e1283b09e9cffbb1bf33ca3
ARG UBUNTU_RUNTIME_IMAGE=ubuntu:24.04@sha256:008173c23f95b170204355c12626cb5a965d779a7e1283b09e9cffbb1bf33ca3
FROM ${UBUNTU_IMAGE} AS build
ARG DEBIAN_FRONTEND=noninteractive
ARG UBUNTU_SNAPSHOT=20260928T000000Z
ARG BTCX_NODE_COMMIT=005bf0098e217b76a2627bfae458dff4f5718dd5
ARG BTCX_CORE_COMMIT=b88b852644f629cd5f25b3424d11b462462c24b3
ARG BUILD_JOBS=2
RUN sed -i -E "s#https?://(archive|security).ubuntu.com/ubuntu/?#http://snapshot.ubuntu.com/ubuntu/${UBUNTU_SNAPSHOT}/#g" /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Check-Valid-Until=false -o Acquire::https::Verify-Peer=false update \
    && apt-get -o Acquire::https::Verify-Peer=false install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install -y --no-install-recommends \
       build-essential cmake git pkg-config python3 libboost-dev libevent-dev \
       libsqlite3-dev libminiupnpc-dev libnatpmp-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git init bitcoin-pocx \
    && git -C bitcoin-pocx remote add origin https://github.com/PoC-Consortium/bitcoin-pocx.git \
    && git -C bitcoin-pocx fetch --depth 1 origin "${BTCX_NODE_COMMIT}" \
    && git -C bitcoin-pocx checkout --detach FETCH_HEAD \
    && git -C bitcoin-pocx submodule update --init --depth 1 bitcoin \
    && test "$(git -C bitcoin-pocx/bitcoin rev-parse HEAD)" = "${BTCX_CORE_COMMIT}"
COPY integrations/production/bitcoin-pocx/bitcoin-pocx-v30-blockpart-compat.patch /tmp/blockpart-compat.patch
COPY integrations/production/bitcoin-pocx/bitcoin-pocx-v30-net-processing-compat.patch /tmp/net-processing-compat.patch
RUN git -C /src/bitcoin-pocx/bitcoin apply --check /tmp/blockpart-compat.patch \
    && git -C /src/bitcoin-pocx/bitcoin apply /tmp/blockpart-compat.patch \
    && git -C /src/bitcoin-pocx/bitcoin apply --check /tmp/net-processing-compat.patch \
    && git -C /src/bitcoin-pocx/bitcoin apply /tmp/net-processing-compat.patch \
    && sha256sum /tmp/blockpart-compat.patch /tmp/net-processing-compat.patch
RUN cmake -S /src/bitcoin-pocx/bitcoin -B /src/bitcoin-pocx/bitcoin/build \
      -DENABLE_POCX=ON -DBUILD_GUI=OFF -DBUILD_TESTS=OFF -DWITH_ZMQ=OFF -DENABLE_IPC=OFF \
    && case "${BUILD_JOBS}" in ''|*[!0-9]*) echo "BUILD_JOBS must be a positive integer" >&2; exit 2;; esac \
    && test "${BUILD_JOBS}" -gt 0 \
    && cmake --build /src/bitcoin-pocx/bitcoin/build --target bitcoind bitcoin-cli -j"${BUILD_JOBS}"

FROM ${UBUNTU_RUNTIME_IMAGE}
ARG DEBIAN_FRONTEND=noninteractive
ARG UBUNTU_SNAPSHOT=20260928T000000Z
RUN sed -i -E "s#https?://(archive|security).ubuntu.com/ubuntu/?#http://snapshot.ubuntu.com/ubuntu/${UBUNTU_SNAPSHOT}/#g" /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Check-Valid-Until=false -o Acquire::https::Verify-Peer=false update \
    && apt-get -o Acquire::https::Verify-Peer=false install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install -y --no-install-recommends \
       curl gosu openssl python3 libboost-filesystem1.83.0 libboost-system1.83.0 \
       libevent-2.1-7t64 libevent-core-2.1-7t64 libevent-extra-2.1-7t64 \
       libevent-pthreads-2.1-7t64 libsqlite3-0 libminiupnpc17 libnatpmp1t64 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system bitcoin \
    && useradd --system --gid bitcoin --home-dir /home/bitcoin --create-home bitcoin
COPY --from=build /src/bitcoin-pocx/bitcoin/build/bin/bitcoind /usr/local/bin/bitcoind
COPY --from=build /src/bitcoin-pocx/bitcoin/build/bin/bitcoin-cli /usr/local/bin/bitcoin-cli
COPY integrations/production/bitcoin-pocx/bitcoin.conf.template /etc/bitcoin/bitcoin.conf.template
COPY integrations/production/bitcoin-pocx/entrypoint.sh /usr/local/bin/entrypoint.sh
COPY integrations/production/bitcoin-pocx/healthcheck.sh /usr/local/bin/healthcheck.sh
RUN chmod 755 /usr/local/bin/entrypoint.sh /usr/local/bin/healthcheck.sh \
    && mkdir -p /data /run/btcx-rpc \
    && chown -R bitcoin:bitcoin /data /run/btcx-rpc
VOLUME ["/data", "/run/btcx-rpc"]
HEALTHCHECK --interval=30s --timeout=10s --start-period=2m --retries=12 CMD ["/usr/local/bin/healthcheck.sh"]
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
