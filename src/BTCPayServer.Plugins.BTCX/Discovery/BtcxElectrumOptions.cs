using System.Net;
using BTCPayServer.Plugins.BTCX.Rpc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BTCX.Discovery;

public sealed class BtcxElectrumOptions
{
    public const string SectionName = "BTCX:Electrum";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "tcp://127.0.0.1:50001/";
    public int PollIntervalSeconds { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 2;

    public Uri Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "tcp" || string.IsNullOrEmpty(endpoint.Host) || endpoint.Port is < 1 or > 65535 ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath is not "/" and not "")
            throw new OptionsValidationException(nameof(BtcxElectrumOptions), typeof(BtcxElectrumOptions), ["Endpoint must be a tcp:// host:port without credentials or path."]);
        if (PollIntervalSeconds is < 2 or > 3600 || TimeoutSeconds is < 1 or > 300 || MaxRetries is < 0 or > 5)
            throw new OptionsValidationException(nameof(BtcxElectrumOptions), typeof(BtcxElectrumOptions), ["Electrum polling/timeout/retry options are outside supported bounds."]);
        if (IPAddress.TryParse(endpoint.Host, out var ip) && !BtcxPrivateEndpoint.IsPrivateOrLoopback(ip))
            throw new OptionsValidationException(nameof(BtcxElectrumOptions), typeof(BtcxElectrumOptions), ["Electrum endpoint must be loopback or private; public endpoints are rejected."]);
        return endpoint;
    }
}
