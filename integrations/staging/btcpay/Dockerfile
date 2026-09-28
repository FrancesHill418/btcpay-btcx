ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.401
ARG BTCPAY_BASE_IMAGE=btcpayserver/btcpayserver:2.4.4
FROM ${DOTNET_SDK_IMAGE} AS plugin-build
WORKDIR /src
COPY . .
RUN dotnet restore src/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.csproj \
    && dotnet publish src/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.csproj \
       -c Release --no-restore -o /out

FROM ${BTCPAY_BASE_IMAGE}
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=plugin-build /out/BTCPayServer.Plugins.BTCX.dll /plugins/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.dll
COPY --from=plugin-build /out/BTCPayServer.Plugins.BTCX.json /plugins/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.json
COPY --from=plugin-build /out/BTCPayServer.Plugins.BTCX.deps.json /plugins/BTCPayServer.Plugins.BTCX/BTCPayServer.Plugins.BTCX.deps.json
COPY integrations/staging/btcpay/entrypoint.sh /usr/local/bin/btcpay-btcx-entrypoint.sh
RUN chmod 755 /usr/local/bin/btcpay-btcx-entrypoint.sh
ENTRYPOINT ["/usr/local/bin/btcpay-btcx-entrypoint.sh"]
