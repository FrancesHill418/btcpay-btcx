namespace BTCPayServer.Plugins.BTCX.Rates;

public sealed class ManualBtcxRateSettings
{
    public bool Enabled { get; set; }
    public decimal? BtcxCnyRate { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string Source { get; set; } = ManualRateSettingsService.Source;
}
