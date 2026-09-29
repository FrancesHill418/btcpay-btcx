using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BTCX.Rpc;

public sealed class BtcxRpcOptions
{
    public const string SectionName = "BTCX:RPC";

    public string Endpoint { get; set; } = "http://127.0.0.1:18443/";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? CookieFilePath { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 2;
    public int RetryDelayMilliseconds { get; set; } = 100;

    public Uri Validate() => Validate(Dns.GetHostAddresses);

    internal Uri Validate(Func<string, IPAddress[]> resolveHostname)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(endpoint.Host) || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath is not "/" and not "")
            throw new OptionsValidationException(nameof(BtcxRpcOptions), typeof(BtcxRpcOptions),
                ["Endpoint must be an HTTP(S) origin without embedded credentials, path, query, or fragment."]);

        if (TimeoutSeconds is < 1 or > 300 || MaxRetries is < 0 or > 5 || RetryDelayMilliseconds is < 0 or > 10_000)
            throw new OptionsValidationException(nameof(BtcxRpcOptions), typeof(BtcxRpcOptions),
                ["RPC timeout/retry values are outside the supported bounds."]);

        if (string.IsNullOrWhiteSpace(CookieFilePath))
        {
            if (string.IsNullOrEmpty(Username) || Password is null)
                throw new OptionsValidationException(nameof(BtcxRpcOptions), typeof(BtcxRpcOptions),
                    ["Provide either a node cookie file or both RPC username and password through secret configuration."]);
        }
        else if (!string.IsNullOrEmpty(Username) || Password is not null)
        {
            throw new OptionsValidationException(nameof(BtcxRpcOptions), typeof(BtcxRpcOptions),
                ["Configure either cookie authentication or username/password, not both."]);
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(endpoint.Host, out var ip) ? [ip] : resolveHostname(endpoint.DnsSafeHost);
        }
        catch (SocketException)
        {
            throw new OptionsValidationException(nameof(BtcxRpcOptions), typeof(BtcxRpcOptions),
                ["RPC endpoint hostname could not be resolved to a loopback or private-network address."]);
        }
        if (addresses.Length == 0 || addresses.Any(address => !BtcxPrivateEndpoint.IsPrivateOrLoopback(address)))
            throw new OptionsValidationException(nameof(BtcxRpcOptions), typeof(BtcxRpcOptions),
                ["Every RPC endpoint address must resolve to a loopback or private-network address; public RPC endpoints are rejected."]);

        return endpoint;
    }
}

internal static class BtcxPrivateEndpoint
{
    public static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return IsPrivateOrLoopback(address.MapToIPv4());
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal)
            return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 127 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);

        // IPv6 Unique Local Address range fc00::/7.
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
               (bytes[0] & 0xfe) == 0xfc;
    }
}
