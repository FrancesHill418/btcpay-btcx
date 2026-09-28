FROM debian:trixie@sha256:9cc080028c43b27d2074d63a5f9caf7166d731494965616c1a6d2827a004585c AS build
ARG BINDEX_COMMIT=eda7c70660baa06affef464c7ea1e131c39304f1
ARG ELECTRS_COMMIT=2f78c63e20215e20944767f0901209c4d740fe5b
RUN apt-get update && apt-get install -y --no-install-recommends \
    cargo git ca-certificates librocksdb-dev libclang-dev build-essential pkg-config \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /build
RUN git init bindex-btcx \
    && git -C bindex-btcx remote add origin https://github.com/PoC-Consortium/bindex-btcx.git \
    && git -C bindex-btcx fetch --depth 1 origin "${BINDEX_COMMIT}" \
    && git -C bindex-btcx checkout --detach FETCH_HEAD \
    && git init electrs-btcx \
    && git -C electrs-btcx remote add origin https://github.com/PoC-Consortium/electrs-btcx.git \
    && git -C electrs-btcx fetch --depth 1 origin "${ELECTRS_COMMIT}" \
    && git -C electrs-btcx checkout --detach FETCH_HEAD
ENV ROCKSDB_INCLUDE_DIR=/usr/include
ENV ROCKSDB_LIB_DIR=/usr/lib/x86_64-linux-gnu
RUN cd /build/electrs-btcx && cargo build --release --locked

FROM debian:trixie-slim@sha256:a99cfc517144bc59b1978475ec53b46ecabec7e43635402ee5b77cc54cd1b20a
RUN apt-get update && apt-get install -y --no-install-recommends \
    librocksdb-dev ca-certificates python3 \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --home-dir /home/electrs --create-home electrs
COPY --from=build /build/electrs-btcx/target/release/electrs /usr/local/bin/electrs
COPY integrations/staging/electrs/healthcheck.py /usr/local/bin/electrs-healthcheck.py
RUN chmod 755 /usr/local/bin/electrs /usr/local/bin/electrs-healthcheck.py \
    && mkdir -p /data/electrs \
    && chown -R electrs:electrs /data
USER electrs
VOLUME ["/data"]
ENTRYPOINT ["/usr/local/bin/electrs"]
