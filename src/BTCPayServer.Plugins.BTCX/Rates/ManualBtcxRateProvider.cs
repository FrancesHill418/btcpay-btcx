using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Plugins.BTCX.Rates;

public sealed class ManualBtcxRateProvider(ManualRateSettingsService settingsService) : IContextualRateProvider
{
    public const string ProviderId = "manualbtcx";
    private static readonly CurrencyPair BtcxCny = new(Plugin.CryptoCode, "CNY");
    private readonly ManualRateSettingsService _settingsService = settingsService;

    public RateSourceInfo RateSourceInfo => new(ProviderId, "Manual BTCX/CNY", "https://docs.btcpayserver.org/Development/Plugins/");

    public Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        // RateProviderFactory only calls the contextual overload for invoice rate requests.
        // Returning no rate here prevents an accidental unscoped query from using the manual value.
        return Task.FromResult(Array.Empty<PairRate>());
    }

    public async Task<PairRate[]> GetRatesAsync(IRateContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        var settings = await _settingsService.GetValidEnabledAsync();
        if (settings?.BtcxCnyRate is not decimal rate)
            return Array.Empty<PairRate>();

        return [new PairRate(BtcxCny, new BidAsk(rate, rate))];
    }
}
