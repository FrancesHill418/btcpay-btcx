using System.Security.Cryptography;

namespace BTCPayServer.Plugins.BTCX.Discovery;

/// <summary>Electrum's scripthash is SHA256(scriptPubKey), displayed in reverse byte order.</summary>
public static class BtcxElectrumScriptHash
{
    public static string FromScriptPubKey(ReadOnlySpan<byte> scriptPubKey)
    {
        var digest = SHA256.HashData(scriptPubKey);
        Array.Reverse(digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
