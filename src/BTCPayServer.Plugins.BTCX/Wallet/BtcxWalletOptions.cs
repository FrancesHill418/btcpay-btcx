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
    public bool AllowMainnet { get; set; }

    public (string WalletName, BtcxNetworkId Network) Validate(string? environmentName = null)
    {
        if (string.IsNullOrWhiteSpace(WalletName) || !Regex.IsMatch(WalletName, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
            throw new OptionsValidationException(nameof(BtcxWalletOptions), typeof(BtcxWalletOptions), ["WalletName must be one explicit wallet route segment."]);
        var networkParameters = BtcxNetworkParameters.All.FirstOrDefault(value =>
            string.Equals(Network, value.ChainName, StringComparison.OrdinalIgnoreCase));
        if (networkParameters is null)
            throw new OptionsValidationException(nameof(BtcxWalletOptions), typeof(BtcxWalletOptions), ["Network must be main, test, or regtest."]);
        if (networkParameters.Id == BtcxNetworkId.Mainnet && !AllowMainnet)
            throw new OptionsValidationException(nameof(BtcxWalletOptions), typeof(BtcxWalletOptions),
                ["BTCX mainnet is disabled. Explicitly set BTCX:Wallet:AllowMainnet=true only in an approved mainnet deployment."]);
        if (networkParameters.Id == BtcxNetworkId.Mainnet && AllowMainnet &&
            (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase)))
            throw new OptionsValidationException(nameof(BtcxWalletOptions), typeof(BtcxWalletOptions),
                ["BTCX mainnet cannot be enabled in Development or Staging environments."]);
        return (WalletName, networkParameters.Id);
    }
}
