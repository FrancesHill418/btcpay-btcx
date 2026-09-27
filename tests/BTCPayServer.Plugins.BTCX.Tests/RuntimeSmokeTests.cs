using BTCPayServer.Plugins;
using BTCPayServer.Plugins.BTCX;
using BTCPayServer.Plugins.BTCX.Payments;
using BTCPayServer.Plugins.BTCX.Rates;
using BTCPayServer.Payments;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BTCX.Controllers;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class RuntimeSmokeTests : UnitTestBase
{
    public RuntimeSmokeTests(ITestOutputHelper _) : base(new QuietOutput()) { }

    [Fact]
    public async Task DevelopmentHostLoadsBtcxPluginAndQueriesLiveManualRate()
    {
        if (Environment.GetEnvironmentVariable("BTCX_RUNTIME_SMOKE") != "1")
            Assert.Skip("Set BTCX_RUNTIME_SMOKE=1 and start the isolated development PostgreSQL service to run the BTCPay host smoke test.");

        var oldDebugPlugins = Environment.GetEnvironmentVariable("DEBUG_PLUGINS");
        var oldNoDefaultChain = Environment.GetEnvironmentVariable("BTCPAY_NODEFAULTCHAIN");
        var oldEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var pluginPath = Path.Combine(repoRoot, "src/BTCPayServer.Plugins.BTCX/bin/Debug/net10.0/BTCPayServer.Plugins.BTCX.dll");
        Assert.True(File.Exists(pluginPath), $"Build plugin first: {pluginPath}");

        Environment.SetEnvironmentVariable("DEBUG_PLUGINS", pluginPath);
        Environment.SetEnvironmentVariable("BTCPAY_NODEFAULTCHAIN", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            using var tester = CreateServerTester(newDb: true);
            tester.PayTester.MockRates = false;
            await tester.StartAsync();

            var plugins = tester.PayTester.GetService<PluginService>();
            var btcxPlugin = Assert.Single(plugins.LoadedPlugins, p => p.Identifier == "BTCPayServer.Plugins.BTCX");
            Assert.Equal(typeof(Plugin).Assembly.GetName().Name, btcxPlugin.GetType().Assembly.GetName().Name);

            var network = tester.PayTester.Networks.GetNetwork<BTCXNetwork>(Plugin.CryptoCode);
            Assert.NotNull(network);
            Assert.Equal("BTCX", network.DisplayName);
            Assert.Equal(8, network.Divisibility);

            var handlers = tester.PayTester.GetService<PaymentMethodHandlerDictionary>();
            Assert.IsType<BtcxPaymentMethodHandler>(handlers.TryGet(PaymentTypes.CHAIN.GetPaymentMethodId("BTCX")));

            var provider = Assert.IsType<ManualBtcxRateProvider>(tester.PayTester.GetService<RateProviderFactory>().Providers[ManualBtcxRateProvider.ProviderId]);
            var settings = tester.PayTester.GetService<SettingsRepository>();
            await settings.UpdateSetting(new ManualBtcxRateSettings
            {
                Enabled = true,
                BtcxCnyRate = 0.20m,
                UpdatedAt = DateTimeOffset.UtcNow,
                Source = "manual"
            });

            var query = await tester.PayTester.GetService<RateProviderFactory>().QueryRates(
                ManualBtcxRateProvider.ProviderId, new SmokeRateContext(), TestContext.Current.CancellationToken);
            Assert.Null(query.Exception);
            Assert.Equal(0.20m, Assert.Single(query.PairRates).BidAsk.Bid);

            await settings.UpdateSetting(new ManualBtcxRateSettings
            {
                Enabled = true,
                BtcxCnyRate = 0.25m,
                UpdatedAt = DateTimeOffset.UtcNow,
                Source = "manual"
            });
            query = await tester.PayTester.GetService<RateProviderFactory>().QueryRates(
                ManualBtcxRateProvider.ProviderId, new SmokeRateContext(), TestContext.Current.CancellationToken);
            Assert.Null(query.Exception);
            Assert.Equal(0.25m, Assert.Single(query.PairRates).BidAsk.Bid);

            var settingsController = tester.PayTester.GetController<BtcxSettingsController>(isAdmin: true);
            var settingsPage = await settingsController.Index();
            var actionDescriptors = tester.PayTester.GetService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
            Assert.Contains(actionDescriptors, descriptor => descriptor.AttributeRouteInfo?.Template == "server/btcx");
            var settingsView = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(settingsPage);
            var settingsModel = Assert.IsType<BtcxSettingsViewModel>(settingsView.Model);
            Assert.Equal("0.25", settingsModel.BtcxCnyRate);
            Assert.Equal("manual", settingsModel.Source);
            Assert.NotNull(settingsModel.UpdatedAt);
            settingsController.TempData = new TempDataDictionary(
                settingsController.ControllerContext.HttpContext,
                tester.PayTester.GetService<ITempDataProvider>());
            var saveResult = await settingsController.Save(true, "0.25");
            Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(saveResult);
            var savedSettings = await settings.GetSettingAsync<ManualBtcxRateSettings>();
            Assert.NotNull(savedSettings);
            Assert.Equal(0.25m, savedSettings.BtcxCnyRate);
            Assert.Equal("manual", savedSettings.Source);
            Assert.NotNull(savedSettings.UpdatedAt);

            var account = tester.NewAccount();
            await account.GrantAccessAsync(true);
            var client = await account.CreateClient(Policies.Unrestricted);
            var btcxPaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId("BTCX").ToString();
            await client.UpdateStorePaymentMethod(account.StoreId!, btcxPaymentMethodId, new UpdatePaymentMethodRequest
            {
                Enabled = true,
                Config = new JObject()
            }, TestContext.Current.CancellationToken);

            await settings.UpdateSetting(new ManualBtcxRateSettings
            {
                Enabled = true,
                BtcxCnyRate = 0.20m,
                UpdatedAt = DateTimeOffset.UtcNow,
                Source = "manual"
            });
            var invoiceA = await client.CreateInvoice(account.StoreId!, new CreateInvoiceRequest
            {
                Amount = 7.50m,
                Currency = "CNY",
                Metadata = JObject.FromObject(new { orderId = "runtime-A" }),
                Checkout = new CreateInvoiceRequest.CheckoutOptions { PaymentMethods = [btcxPaymentMethodId] }
            }, TestContext.Current.CancellationToken);
            Assert.NotNull(invoiceA.CheckoutLink);
            Assert.Equal("runtime-A", invoiceA.Metadata["orderId"]?.Value<string>());
            var entityA = await tester.PayTester.InvoiceRepository.GetInvoice(invoiceA.Id);
            var promptA = Assert.Single(entityA.GetPaymentPrompts());
            Assert.Equal(btcxPaymentMethodId, promptA.PaymentMethodId.ToString());
            var snapshotA = Assert.IsType<JObject>(promptA.Details);
            Assert.Equal(7.50m, snapshotA["fiatAmount"]!.Value<decimal>());
            Assert.Equal("CNY", snapshotA["fiatCurrency"]!.Value<string>());
            Assert.Equal(0.20m, snapshotA["exchangeRate"]!.Value<decimal>());
            Assert.Equal(37.5m, snapshotA["cryptoAmount"]!.Value<decimal>());
            Assert.Equal("manual", snapshotA["rateSource"]!.Value<string>());
            var checkoutResponse = await tester.PayTester.HttpClient.GetAsync(invoiceA.CheckoutLink!, TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.OK, checkoutResponse.StatusCode);
            var checkoutHtml = await checkoutResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("BTCX", checkoutHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("37.5", checkoutHtml, StringComparison.Ordinal);

            await settings.UpdateSetting(new ManualBtcxRateSettings
            {
                Enabled = true,
                BtcxCnyRate = 0.25m,
                UpdatedAt = DateTimeOffset.UtcNow,
                Source = "manual"
            });
            var invoiceB = await client.CreateInvoice(account.StoreId!, new CreateInvoiceRequest
            {
                Amount = 7.50m,
                Currency = "CNY",
                Metadata = JObject.FromObject(new { orderId = "runtime-B" }),
                Checkout = new CreateInvoiceRequest.CheckoutOptions { PaymentMethods = [btcxPaymentMethodId] }
            }, TestContext.Current.CancellationToken);
            var entityB = await tester.PayTester.InvoiceRepository.GetInvoice(invoiceB.Id);
            var promptB = Assert.Single(entityB.GetPaymentPrompts());
            var snapshotB = Assert.IsType<JObject>(promptB.Details);
            Assert.Equal(0.20m, snapshotA["exchangeRate"]!.Value<decimal>());
            Assert.Equal(0.25m, snapshotB["exchangeRate"]!.Value<decimal>());
            Assert.Equal(30m, snapshotB["cryptoAmount"]!.Value<decimal>());

            await settings.UpdateSetting(new ManualBtcxRateSettings { Enabled = false });
            query = await tester.PayTester.GetService<RateProviderFactory>().QueryRates(
                ManualBtcxRateProvider.ProviderId, new SmokeRateContext(), TestContext.Current.CancellationToken);
            Assert.Empty(query.PairRates);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await tester.PayTester.HttpClient.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEBUG_PLUGINS", oldDebugPlugins);
            Environment.SetEnvironmentVariable("BTCPAY_NODEFAULTCHAIN", oldNoDefaultChain);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", oldEnvironment);
        }
    }

    private sealed class SmokeRateContext : IRateContext { }

    private sealed class QuietOutput : ITestOutputHelper
    {
        public string Output => string.Empty;
        public void Write(string message) { }
        public void Write(string format, params object[] args) { }
        public void WriteLine(string message) { }
        public void WriteLine(string format, params object[] args) { }
    }
}
