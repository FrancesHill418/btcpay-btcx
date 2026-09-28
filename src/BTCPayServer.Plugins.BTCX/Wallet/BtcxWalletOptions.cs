using System.Text.RegularExpressions;
using BTCPayServer.Plugins.BTCX;
using BTCPayServer.Plugins.BTCX.Rpc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BTCX.Wallet;

public sealed class BtcxWalletOptions
{
    public const string SectionName = "BTCX:Wallet";
    public string WalletName { get; set; } = "btcx-receive";
    public string Network { get; set; } = "regtest";

    public (string WalletName, BtcxNetworkId Network) Validate()
    {
        if (string.IsNullOrWhiteSpace(WalletName) || !Regex.IsMatch(WalletName, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
            throw new OptionsValidationException(nameof(BtcxWalletOptions), typeof(BtcxWalletOptions), ["WalletName must be one explicit wallet route segment."]);
        var networkParameters = BtcxNetworkParameters.All.FirstOrDefault(value =>
            string.Equals(Network, value.ChainName, StringComparison.OrdinalIgnoreCase));
        if (networkParameters is null)
            throw new OptionsValidationException(nameof(BtcxWalletOptions), typeof(BtcxWalletOptions), ["Network must be main, test, or regtest."]);
        return (WalletName, networkParameters.Id);
    }
}
