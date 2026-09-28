using System.Globalization;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.BTCX.Controllers;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Payments;
using BTCPayServer.Plugins.BTCX.Rates;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class ManualBtcxRateTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0.20")]
    [InlineData("0.25")]
    public async Task ContextualProviderReturnsConfiguredRate(string rateText)
    {
        var settings = new FakeSettingsRepository(EnabledSettings(decimal.Parse(rateText, CultureInfo.InvariantCulture)));
        var provider = CreateProvider(settings);

        var result = await provider.GetRatesAsync(new StoreIdRateContext("store"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new CurrencyPair("BTCX", "CNY"), result[0].CurrencyPair);
        Assert.Equal(decimal.Parse(rateText, CultureInfo.InvariantCulture), result[0].BidAsk.Bid);
        Assert.Equal(result[0].BidAsk.Bid, result[0].BidAsk.Ask);
    }

    [Fact]
    public async Task ProviderReadsNewValueImmediatelyAfterSettingsUpdate()
    {
        var settings = new FakeSettingsRepository(EnabledSettings(0.20m));
        var provider = CreateProvider(settings);
        var factory = CreateFactory(provider);
        var context = new StoreIdRateContext("store");

        Assert.Equal(0.20m, Assert.Single((await factory.QueryRates(ManualBtcxRateProvider.ProviderId, context,
            TestContext.Current.CancellationToken)).PairRates).BidAsk.Bid);
        await settings.UpdateSetting(EnabledSettings(0.25m));
        Assert.Equal(0.25m, Assert.Single((await factory.QueryRates(ManualBtcxRateProvider.ProviderId, context,
            TestContext.Current.CancellationToken)).PairRates).BidAsk.Bid);
    }

    [Fact]
    public async Task UnscopedProviderQueryReturnsNoRate()
    {
        var provider = CreateProvider(new FakeSettingsRepository(EnabledSettings(0.20m)));
        Assert.Empty(await provider.GetRatesAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false, "0.20")]
    [InlineData(true, "0")]
    [InlineData(true, "-0.01")]
    [InlineData(true, "0.123456789")]
    public async Task DisabledOrInvalidRateReturnsNoRate(bool enabled, string rateText)
    {
        decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var rateValue);
        var settings = new FakeSettingsRepository(new ManualBtcxRateSettings
        {
            Enabled = enabled,
            BtcxCnyRate = rateValue,
            UpdatedAt = Timestamp,
            Source = ManualRateSettingsService.Source
        });
        var provider = CreateProvider(settings);
        Assert.Empty(await provider.GetRatesAsync(new StoreIdRateContext("store"), CancellationToken.None));
    }

    [Fact]
    public async Task MissingConfigurationReturnsNoRate()
    {
        var provider = CreateProvider(new FakeSettingsRepository(null));
        Assert.Empty(await provider.GetRatesAsync(new StoreIdRateContext("store"), CancellationToken.None));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.25")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0.123456789")]
    public void RateInputValidationRejectsInvalidValues(string value)
    {
        Assert.False(ManualRateSettingsService.TryParseRate(value, out _));
    }

    [Theory]
    [InlineData("7.50", "0.20", "37.5")]
    [InlineData("7.50", "0.25", "30.0")]
    public void CalculatesExpectedBtcxAmount(string fiatAmountText, string rateText, string expectedText)
    {
        var fiatAmount = decimal.Parse(fiatAmountText, CultureInfo.InvariantCulture);
        var rate = decimal.Parse(rateText, CultureInfo.InvariantCulture);
        var expected = decimal.Parse(expectedText, CultureInfo.InvariantCulture);
        Assert.Equal(expected, BtcxAmountCalculator.Calculate(fiatAmount, rate, Plugin.Divisibility));
    }

    [Fact]
    public void DecimalPrecisionIsPreserved()
    {
        Assert.Equal(33.33333334m, BtcxAmountCalculator.Calculate(7.50m, 0.225m, Plugin.Divisibility));
    }

    [Fact]
    public void RoundsUpToAvoidUnderpayment()
    {
        Assert.Equal(0.00000001m, BtcxAmountCalculator.Calculate(0.000000001m, 0.20m, Plugin.Divisibility));
    }

    [Fact]
    public void VerySmallCryptoAmountIsAtLeastOneBaseUnit()
    {
        Assert.Equal(BtcxAmountCalculator.MinimumUnit(Plugin.Divisibility),
            BtcxAmountCalculator.Calculate(0.0000000001m, 1m, Plugin.Divisibility));
    }

    [Fact]
    public void AmountAboveBtcxConsensusMaximumIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BtcxAmountCalculator.Calculate(1_000_000_000m, 0.20m, Plugin.Divisibility));
    }

    [Fact]
    public void MaximumConsensusAmountCanBeCalculated()
    {
        Assert.Equal(21_000_000m, BtcxAmountCalculator.Calculate(21_000_000m, 1m, Plugin.Divisibility));
    }

    [Fact]
    public void RejectsOverflowingAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BtcxAmountCalculator.Calculate(decimal.MaxValue, 0.00000001m, Plugin.Divisibility));
    }

    [Fact]
    public async Task InvoiceSnapshotRetainsOriginalRateAfterProviderUpdate()
    {
        var settings = new FakeSettingsRepository(EnabledSettings(0.20m));
        var provider = CreateProvider(settings);
        var context = new StoreIdRateContext("store");
        var initialRate = Assert.Single(await provider.GetRatesAsync(context, CancellationToken.None)).BidAsk.Bid;
        var oldInvoice = new BtcxInvoiceSnapshot(7.50m, "CNY", 37.5m, "BTCX", initialRate,
            ManualRateSettingsService.Source, settings.Value!.UpdatedAt!.Value);

        await settings.UpdateSetting(EnabledSettings(0.25m, Timestamp.AddMinutes(1)));
        var newRate = Assert.Single(await provider.GetRatesAsync(context, CancellationToken.None)).BidAsk.Bid;

        Assert.Equal(0.20m, oldInvoice.ExchangeRate);
        Assert.Equal(37.5m, oldInvoice.CryptoAmount);
        Assert.Equal(0.25m, newRate);
    }

    [Fact]
    public async Task InvoiceSnapshotContainsRequiredAuditFields()
    {
        var settings = new FakeSettingsRepository(EnabledSettings(0.20m));
        var snapshot = new BtcxInvoiceSnapshot(7.50m, "CNY", 37.5m, "BTCX", 0.20m,
            ManualRateSettingsService.Source, settings.Value!.UpdatedAt!.Value);

        Assert.Equal(7.50m, snapshot.FiatAmount);
        Assert.Equal("CNY", snapshot.FiatCurrency);
        Assert.Equal(37.5m, snapshot.CryptoAmount);
        Assert.Equal("BTCX", snapshot.CryptoCurrency);
        Assert.Equal(0.20m, snapshot.ExchangeRate);
        Assert.Equal("manual", snapshot.RateSource);
        Assert.Equal(Timestamp, snapshot.RateTimestamp);
    }

    [Fact]
    public async Task SaveUpdatesTimestampAndSource()
    {
        var settings = new FakeSettingsRepository(null);
        var service = new ManualRateSettingsService(settings);
        var saved = await service.SaveAsync(true, 0.20m);
        Assert.True(saved.Enabled);
        Assert.Equal(0.20m, saved.BtcxCnyRate);
        Assert.Equal("manual", saved.Source);
        Assert.NotNull(saved.UpdatedAt);
        Assert.True(ManualRateSettingsService.IsValid(saved));
    }

    [Fact]
    public void PluginEntryPointRegistersPaymentMethodAndContextualProvider()
    {
        Assert.IsAssignableFrom<BaseBTCPayServerPlugin>(new Plugin());
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ISettingsRepository>(new FakeSettingsRepository(EnabledSettings(0.20m)));
        new Plugin().Execute(services);

        using var provider = services.BuildServiceProvider();
        var rateProvider = Assert.Single(provider.GetServices<IRateProvider>());
        Assert.IsAssignableFrom<IContextualRateProvider>(rateProvider);
        Assert.Equal(ManualBtcxRateProvider.ProviderId, rateProvider.RateSourceInfo.Id);

        var handler = Assert.Single(provider.GetServices<IPaymentMethodHandler>());
        Assert.Equal(PaymentTypes.CHAIN.GetPaymentMethodId("BTCX"), handler.PaymentMethodId);
        Assert.Single(provider.GetServices<IPaymentLinkExtension>());
        Assert.Single(provider.GetServices<ICheckoutModelExtension>());
        Assert.Equal("BTCX", Assert.Single(provider.GetServices<BTCPayNetworkBase>()).CryptoCode);
        Assert.Equal("BTCPayServer.Plugins.BTCX", new Plugin().Identifier);
        Assert.Contains(new Plugin().Dependencies, dependency => dependency.Condition == ">=2.4.4");
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "BTCPayServer.Plugins.BTCX.json")));
    }

    [Fact]
    public async Task SettingsUiSavesRateForFutureInvoices()
    {
        var repository = new FakeSettingsRepository(EnabledSettings(0.20m));
        var controller = CreateSettingsController(repository);

        var result = await controller.Save(true, "0.25");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(repository.Value!.Enabled);
        Assert.Equal(0.25m, repository.Value.BtcxCnyRate);
        Assert.Equal("manual", repository.Value.Source);
        Assert.True(repository.Value.UpdatedAt > Timestamp);
    }

    [Fact]
    public async Task SettingsUiRejectsZeroRateWithoutOverwritingLastValue()
    {
        var repository = new FakeSettingsRepository(EnabledSettings(0.20m));
        var controller = CreateSettingsController(repository);

        var result = await controller.Save(true, "0");

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0.20m, repository.Value!.BtcxCnyRate);
    }

    [Fact]
    public async Task SaveRejectsInvalidRatesWhenEnabled()
    {
        var service = new ManualRateSettingsService(new FakeSettingsRepository(null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveAsync(true, 0m));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveAsync(true, -0.01m));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveAsync(true, 0.123456789m));
    }

    [Fact]
    public async Task MissingTimestampIsInvalid()
    {
        var settings = EnabledSettings(0.20m);
        settings.UpdatedAt = null;
        var provider = CreateProvider(new FakeSettingsRepository(settings));
        Assert.Empty(await provider.GetRatesAsync(new StoreIdRateContext("store"), CancellationToken.None));
    }

    private static ManualBtcxRateProvider CreateProvider(FakeSettingsRepository settings) =>
        new(new ManualRateSettingsService(settings));

    private static RateProviderFactory CreateFactory(ManualBtcxRateProvider provider) =>
        new(new FakeHttpClientFactory(), [provider]);

    private static BtcxSettingsController CreateSettingsController(FakeSettingsRepository repository)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new BtcxSettingsController(repository, new ManualRateSettingsService(repository),
            NullLogger<BtcxSettingsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new FakeTempDataProvider())
        };
        return controller;
    }

    private static ManualBtcxRateSettings EnabledSettings(decimal rate, DateTimeOffset? updatedAt = null) => new()
    {
        Enabled = true,
        BtcxCnyRate = rate,
        UpdatedAt = updatedAt ?? Timestamp,
        Source = ManualRateSettingsService.Source
    };

    private sealed class FakeSettingsRepository(ManualBtcxRateSettings? value) : ISettingsRepository
    {
        public ManualBtcxRateSettings? Value { get; private set; } = value;

        public Task<T?> GetSettingAsync<T>(string? name = null) where T : class => Task.FromResult(Value as T);

        public Task UpdateSetting<T>(T obj, string? name = null) where T : class
        {
            Value = obj as ManualBtcxRateSettings ?? throw new InvalidOperationException("Unexpected settings type");
            return Task.CompletedTask;
        }

        public Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default) where T : class =>
            throw new NotSupportedException();
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
