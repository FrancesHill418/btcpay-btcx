using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Payments;
using BTCPayServer.Plugins.BTCX.Rates;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.BTCX;

public sealed class Plugin : BaseBTCPayServerPlugin
{
    public const string Area = "BTCX";
    public const string CryptoCode = "BTCX";
    public const int Divisibility = BtcxAmount.Decimals;

    public override string Identifier => "BTCPayServer.Plugins.BTCX";
    public override string Name => "BTCX";
    public override string Description => "BTCX payment method skeleton with an administrator managed CNY rate.";
    public override Version Version => new(0, 1, 0);
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies =>
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.4.4" }
    ];

    public override void Execute(IServiceCollection services)
    {
        var network = new BTCXNetwork();
        services.AddBTCPayNetwork(network);
        services.AddCurrencyData(new CurrencyData
        {
            Code = CryptoCode,
            Name = "BTCX",
            Symbol = "BTCX",
            Divisibility = Divisibility,
            Crypto = true
        });
        services.AddSingleton<ManualRateSettingsService>();
        services.AddSingleton<IRateProvider, ManualBtcxRateProvider>();
        services.AddSingleton<IPaymentMethodHandler, BtcxPaymentMethodHandler>();
        services.AddSingleton<IPaymentLinkExtension, BtcxPaymentLinkExtension>();
        services.AddSingleton<ICheckoutModelExtension, BtcxCheckoutModelExtension>();
        services.AddStaticSearch(new ActionResultItemViewModel
        {
            RequiredPolicy = Policies.CanModifyServerSettings,
            Title = "Configure BTCX/CNY rate",
            Action = nameof(Controllers.BtcxSettingsController.Index),
            Controller = "BtcxSettings",
            Values = _ => new { area = Area },
            Category = "Server",
            Keywords = ["BTCX", "rate", "payment"]
        });
    }
}
