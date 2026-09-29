# Minimal runtime wrapper for secret-file injection and HTTP health probing.
# BTCPay Server core remains the immutable upstream v2.4.4 image.
ARG BTCPAY_BASE_IMAGE=btcpayserver/btcpayserver:2.4.4@sha256:c264aa08cd32a469bd30d41978b73dc8bb2de1503ce67bdb0ab8fd5d934fb614
FROM ${BTCPAY_BASE_IMAGE}
ARG DEBIAN_FRONTEND=noninteractive
ARG UBUNTU_SNAPSHOT=20260928T000000Z
USER root
RUN sed -i -E "s#https?://(archive|security).ubuntu.com/ubuntu/?#http://snapshot.ubuntu.com/ubuntu/${UBUNTU_SNAPSHOT}/#g" /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Check-Valid-Until=false -o Acquire::https::Verify-Peer=false update \
    && apt-get -o Acquire::https::Verify-Peer=false install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY integrations/production/btcpay/entrypoint.sh /usr/local/bin/btcpay-btcx-entrypoint.sh
RUN chmod 755 /usr/local/bin/btcpay-btcx-entrypoint.sh
ENTRYPOINT ["/usr/local/bin/btcpay-btcx-entrypoint.sh"]
