# BTCX electrs candidate. Debian package availability is fixed by the snapshot;
# Rust dependencies are locked by the electrs commit's Cargo.lock.
ARG DEBIAN_IMAGE=debian:trixie@sha256:9cc080028c43b27d2074d63a5f9caf7166d731494965616c1a6d2827a004585c
ARG DEBIAN_RUNTIME_IMAGE=debian:trixie-slim@sha256:a99cfc517144bc59b1978475ec53b46ecabec7e43635402ee5b77cc54cd1b20a
FROM ${DEBIAN_IMAGE} AS build
ARG DEBIAN_FRONTEND=noninteractive
ARG DEBIAN_SNAPSHOT=20260928T000000Z
ARG BINDEX_COMMIT=eda7c70660baa06affef464c7ea1e131c39304f1
ARG ELECTRS_COMMIT=2f78c63e20215e20944767f0901209c4d740fe5b
RUN sed -i -E \
       -e "s#https?://deb.debian.org/debian-security/?#http://snapshot.debian.org/archive/debian-security/${DEBIAN_SNAPSHOT}/#g" \
       -e "s#https?://security.debian.org/debian-security/?#http://snapshot.debian.org/archive/debian-security/${DEBIAN_SNAPSHOT}/#g" \
       -e "s#https?://deb.debian.org/debian/?#http://snapshot.debian.org/archive/debian/${DEBIAN_SNAPSHOT}/#g" \
       /etc/apt/sources.list.d/debian.sources \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install -y --no-install-recommends \
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
    && git -C electrs-btcx checkout --detach FETCH_HEAD \
    && test -f electrs-btcx/Cargo.lock \
    && grep -F 'bindex = { path = "../bindex-btcx/bindex-lib"' electrs-btcx/Cargo.toml
ENV ROCKSDB_INCLUDE_DIR=/usr/include
ENV ROCKSDB_LIB_DIR=/usr/lib/x86_64-linux-gnu
RUN cd /build/electrs-btcx && cargo build --release --locked

FROM ${DEBIAN_RUNTIME_IMAGE}
ARG DEBIAN_FRONTEND=noninteractive
ARG DEBIAN_SNAPSHOT=20260928T000000Z
RUN sed -i -E \
       -e "s#https?://deb.debian.org/debian-security/?#http://snapshot.debian.org/archive/debian-security/${DEBIAN_SNAPSHOT}/#g" \
       -e "s#https?://security.debian.org/debian-security/?#http://snapshot.debian.org/archive/debian-security/${DEBIAN_SNAPSHOT}/#g" \
       -e "s#https?://deb.debian.org/debian/?#http://snapshot.debian.org/archive/debian/${DEBIAN_SNAPSHOT}/#g" \
       /etc/apt/sources.list.d/debian.sources \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install -y --no-install-recommends librocksdb-dev ca-certificates python3 \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --home-dir /home/electrs --create-home electrs
COPY --from=build /build/electrs-btcx/target/release/electrs /usr/local/bin/electrs
COPY integrations/production/electrs/healthcheck.py /usr/local/bin/electrs-healthcheck.py
RUN chmod 755 /usr/local/bin/electrs /usr/local/bin/electrs-healthcheck.py \
    && mkdir -p /data/electrs \
    && chown -R electrs:electrs /data
USER electrs
VOLUME ["/data"]
HEALTHCHECK --interval=20s --timeout=5s --start-period=2m --retries=12 CMD ["python3", "/usr/local/bin/electrs-healthcheck.py", "127.0.0.1", "50001"]
ENTRYPOINT ["/usr/local/bin/electrs"]
