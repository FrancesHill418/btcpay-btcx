FROM ubuntu:24.04 AS build
ARG DEBIAN_FRONTEND=noninteractive
ARG BTCX_NODE_COMMIT=005bf0098e217b76a2627bfae458dff4f5718dd5
ARG BTCX_CORE_COMMIT=b88b852644f629cd5f25b3424d11b462462c24b3
ARG BUILD_JOBS=2
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential cmake git pkg-config python3 libboost-dev libevent-dev \
    libsqlite3-dev libminiupnpc-dev libnatpmp-dev ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git init bitcoin-pocx \
    && git -C bitcoin-pocx remote add origin https://github.com/PoC-Consortium/bitcoin-pocx.git \
    && git -C bitcoin-pocx fetch --depth 1 origin "${BTCX_NODE_COMMIT}" \
    && git -C bitcoin-pocx checkout --detach FETCH_HEAD \
    && git -C bitcoin-pocx submodule update --init --depth 1 bitcoin \
    && test "$(git -C bitcoin-pocx/bitcoin rev-parse HEAD)" = "${BTCX_CORE_COMMIT}"
COPY integrations/electrs-btcx/bitcoin-pocx-v30-blockpart-compat.patch /tmp/blockpart-compat.patch
COPY integrations/electrs-btcx/bitcoin-pocx-v30-net-processing-compat.patch /tmp/net-processing-compat.patch
RUN git -C /src/bitcoin-pocx/bitcoin apply --check /tmp/blockpart-compat.patch \
    && git -C /src/bitcoin-pocx/bitcoin apply /tmp/blockpart-compat.patch \
    && git -C /src/bitcoin-pocx/bitcoin apply --check /tmp/net-processing-compat.patch \
    && git -C /src/bitcoin-pocx/bitcoin apply /tmp/net-processing-compat.patch
RUN cmake -S /src/bitcoin-pocx/bitcoin -B /src/bitcoin-pocx/bitcoin/build \
      -DENABLE_POCX=ON -DBUILD_GUI=OFF -DBUILD_TESTS=OFF -DWITH_ZMQ=OFF -DENABLE_IPC=OFF \
    && case "${BUILD_JOBS}" in ''|*[!0-9]*) echo "BUILD_JOBS must be a positive integer" >&2; exit 2;; esac \
    && test "${BUILD_JOBS}" -gt 0 \
    && cmake --build /src/bitcoin-pocx/bitcoin/build --target bitcoind bitcoin-cli -j"${BUILD_JOBS}"

FROM ubuntu:24.04
ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates gosu libboost-filesystem1.83.0 libboost-system1.83.0 \
    libevent-2.1-7t64 libevent-core-2.1-7t64 libevent-extra-2.1-7t64 \
    libevent-pthreads-2.1-7t64 libsqlite3-0 libminiupnpc17 libnatpmp1t64 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system bitcoin \
    && useradd --system --gid bitcoin --home-dir /home/bitcoin --create-home bitcoin
COPY --from=build /src/bitcoin-pocx/bitcoin/build/bin/bitcoind /usr/local/bin/bitcoind
COPY --from=build /src/bitcoin-pocx/bitcoin/build/bin/bitcoin-cli /usr/local/bin/bitcoin-cli
COPY integrations/staging/bitcoin-pocx/bitcoin.conf.template /etc/bitcoin/bitcoin.conf.template
COPY integrations/staging/bitcoin-pocx/entrypoint.sh /usr/local/bin/entrypoint.sh
COPY integrations/staging/bitcoin-pocx/wallet-init.sh /usr/local/bin/wallet-init.sh
RUN chmod 755 /usr/local/bin/entrypoint.sh /usr/local/bin/wallet-init.sh \
    && mkdir -p /data /run/btcx-rpc \
    && chown -R bitcoin:bitcoin /data /run/btcx-rpc
VOLUME ["/data"]
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
