using BTCPayServer;

namespace BTCPayServer.Plugins.BTCX;

public sealed class BTCXNetwork : BTCPayNetworkBase
{
    public IReadOnlyList<BtcxNetworkParameters> Networks => BtcxNetworkParameters.All;

    public BTCXNetwork()
    {
        CryptoCode = Plugin.CryptoCode;
        DisplayName = "BTCX";
        Divisibility = Plugin.Divisibility;
        CryptoImagePath = "btcx.svg";
        DefaultRateRules = ["BTCX_CNY = manualbtcx(BTCX_CNY)"];
        ShowSyncSummary = false;
    }
}
