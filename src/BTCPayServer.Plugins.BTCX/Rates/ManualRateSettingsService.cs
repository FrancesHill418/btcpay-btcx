using System.Globalization;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.BTCX.Rates;

public sealed class ManualRateSettingsService(ISettingsRepository settingsRepository)
{
    public const string Source = "manual";
    public const int MaximumFractionalDigits = 8;
    private readonly ISettingsRepository _settingsRepository = settingsRepository;

    public async Task<ManualBtcxRateSettings> GetAsync()
    {
        return await _settingsRepository.GetSettingAsync<ManualBtcxRateSettings>()
               ?? new ManualBtcxRateSettings();
    }

    public async Task<ManualBtcxRateSettings?> GetValidEnabledAsync()
    {
        var settings = await GetAsync();
        return IsValid(settings) ? settings : null;
    }

    public static bool IsValid(ManualBtcxRateSettings? settings)
    {
        if (settings is not { Enabled: true, BtcxCnyRate: > 0m, UpdatedAt: not null })
            return false;

        if (!string.Equals(settings.Source, Source, StringComparison.Ordinal))
            return false;

        var updatedAt = settings.UpdatedAt.Value;
        if (updatedAt.Offset != TimeSpan.Zero || updatedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return false;

        return GetSignificantScale(settings.BtcxCnyRate.Value) <= MaximumFractionalDigits;
    }

    public static bool TryParseRate(string? value, out decimal rate)
    {
        rate = default;
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate)
               && rate > 0m
               && GetSignificantScale(rate) <= MaximumFractionalDigits;
    }

    public async Task<ManualBtcxRateSettings> SaveAsync(bool enabled, decimal? rate)
    {
        if (enabled && (rate is null || rate <= 0m || GetSignificantScale(rate.Value) > MaximumFractionalDigits))
            throw new ArgumentOutOfRangeException(nameof(rate), "Enter a positive BTCX/CNY rate with no more than eight meaningful decimal places.");

        var settings = new ManualBtcxRateSettings
        {
            Enabled = enabled,
            BtcxCnyRate = rate,
            UpdatedAt = DateTimeOffset.UtcNow,
            Source = Source
        };
        await _settingsRepository.UpdateSetting(settings);
        return settings;
    }

    private static int GetSignificantScale(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0x7F;
        while (scale > 0 && value == decimal.Round(value, scale - 1, MidpointRounding.ToZero))
            scale--;
        return scale;
    }
}
