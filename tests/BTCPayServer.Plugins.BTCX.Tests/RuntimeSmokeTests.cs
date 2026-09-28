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
using NBitcoin;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class RuntimeSmokeTests : UnitTestBase
{
    public RuntimeSmokeTests(ITestOutputHelper _) : base(new QuietOutput()) { }

    [Fact]
    public async Task DevelopmentHostLoadsBtcxPluginAndQueriesLiveManualRate()
    {
        if (Environment.GetEnvironmentVariable("BTCX_RUNTIME_SMOKE") != "1" &&
            Environment.GetEnvironmentVariable("BTCPAY_RUNTIME_SMOKE") != "1")
            Assert.Skip("Set BTCX_RUNTIME_SMOKE=1 (or BTCPAY_RUNTIME_SMOKE=1) and configure isolated development PostgreSQL to run the BTCPay host smoke test.");

        var oldDebugPlugins = Environment.GetEnvironmentVariable("DEBUG_PLUGINS");
        var oldNoDefaultChain = Environment.GetEnvironmentVariable("BTCPAY_NODEFAULTCHAIN");
        var oldEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var oldRpcEndpoint = Environment.GetEnvironmentVariable("BTCX__RPC__Endpoint");
        var oldRpcUsername = Environment.GetEnvironmentVariable("BTCX__RPC__Username");
        var oldRpcPassword = Environment.GetEnvironmentVariable("BTCX__RPC__Password");
        var oldRpcCookiePath = Environment.GetEnvironmentVariable("BTCX__RPC__CookieFilePath");
        var oldWalletNetwork = Environment.GetEnvironmentVariable("BTCX__Wallet__Network");
        var oldElectrumEnabled = Environment.GetEnvironmentVariable("BTCX__Electrum__Enabled");
        var oldElectrumEndpoint = Environment.GetEnvironmentVariable("BTCX__Electrum__Endpoint");
        var oldElectrumPoll = Environment.GetEnvironmentVariable("BTCX__Electrum__PollIntervalSeconds");
        var oldElectrumTimeout = Environment.GetEnvironmentVariable("BTCX__Electrum__TimeoutSeconds");
        var oldElectrumRetries = Environment.GetEnvironmentVariable("BTCX__Electrum__MaxRetries");
        var runtimeNodeCookie = Environment.GetEnvironmentVariable("BTCX_RUNTIME_NODE_COOKIE");
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var pluginPath = Path.Combine(repoRoot, "src/BTCPayServer.Plugins.BTCX/bin/Debug/net10.0/BTCPayServer.Plugins.BTCX.dll");
        Assert.True(File.Exists(pluginPath), $"Build plugin first: {pluginPath}");

        await using var mockRpc = runtimeNodeCookie is null ? new RuntimeWalletRpcMock() : null;
        await using var mockElectrum = runtimeNodeCookie is null ? null : new RuntimeElectrumMock();
        if (mockRpc is not null)
            await mockRpc.StartAsync();
        if (mockElectrum is not null)
            await mockElectrum.StartAsync();
        Environment.SetEnvironmentVariable("DEBUG_PLUGINS", pluginPath);
        Environment.SetEnvironmentVariable("BTCPAY_NODEFAULTCHAIN", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("BTCX__RPC__Endpoint", "http://127.0.0.1:18443/");
        Environment.SetEnvironmentVariable("BTCX__RPC__Username", runtimeNodeCookie is null ? "runtime-smoke" : null);
        Environment.SetEnvironmentVariable("BTCX__RPC__Password", runtimeNodeCookie is null ? "ephemeral-test-only" : null);
        Environment.SetEnvironmentVariable("BTCX__RPC__CookieFilePath", runtimeNodeCookie);
        if (mockRpc is not null)
            Environment.SetEnvironmentVariable("BTCX__RPC__Endpoint", mockRpc.Endpoint);
        Environment.SetEnvironmentVariable("BTCX__Wallet__Network", "regtest");
        Environment.SetEnvironmentVariable("BTCX__Electrum__Enabled", mockElectrum is null ? "false" : "true");
        if (mockElectrum is not null)
        {
            Environment.SetEnvironmentVariable("BTCX__Electrum__Endpoint", mockElectrum.Endpoint);
            Environment.SetEnvironmentVariable("BTCX__Electrum__PollIntervalSeconds", "2");
            Environment.SetEnvironmentVariable("BTCX__Electrum__TimeoutSeconds", "5");
            Environment.SetEnvironmentVariable("BTCX__Electrum__MaxRetries", "0");
        }
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
                Checkout = new CreateInvoiceRequest.CheckoutOptions
                {
                    PaymentMethods = [btcxPaymentMethodId],
                    SpeedPolicy = BTCPayServer.Client.Models.SpeedPolicy.LowSpeed,
                    PaymentTolerance = 0
                }
            }, TestContext.Current.CancellationToken);
            Assert.NotNull(invoiceA.CheckoutLink);
            Assert.Equal("runtime-A", invoiceA.Metadata["orderId"]?.Value<string>());
            var retrievedA = await client.GetInvoice(invoiceA.Id, includePaymentMethods: true, token: TestContext.Current.CancellationToken);
            var retrievedMethod = Assert.Single(retrievedA.PaymentMethods);
            Assert.Equal(btcxPaymentMethodId, retrievedMethod.PaymentMethodId);
            Assert.Equal(BtcxAddressType.WitnessV0, BtcxAddress.Parse(retrievedMethod.Destination, BtcxNetworkId.Regtest).Type);
            Assert.StartsWith("btcx:", retrievedMethod.PaymentLink, StringComparison.Ordinal);
            if (runtimeNodeCookie is not null && mockElectrum is not null)
                await ExerciseRegtestPaymentAsync(client, invoiceA.Id, retrievedMethod.Destination, runtimeNodeCookie, mockElectrum);
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
            Environment.SetEnvironmentVariable("BTCX__RPC__Endpoint", oldRpcEndpoint);
            Environment.SetEnvironmentVariable("BTCX__RPC__Username", oldRpcUsername);
            Environment.SetEnvironmentVariable("BTCX__RPC__Password", oldRpcPassword);
            Environment.SetEnvironmentVariable("BTCX__RPC__CookieFilePath", oldRpcCookiePath);
            Environment.SetEnvironmentVariable("BTCX__Wallet__Network", oldWalletNetwork);
            Environment.SetEnvironmentVariable("BTCX__Electrum__Enabled", oldElectrumEnabled);
            Environment.SetEnvironmentVariable("BTCX__Electrum__Endpoint", oldElectrumEndpoint);
            Environment.SetEnvironmentVariable("BTCX__Electrum__PollIntervalSeconds", oldElectrumPoll);
            Environment.SetEnvironmentVariable("BTCX__Electrum__TimeoutSeconds", oldElectrumTimeout);
            Environment.SetEnvironmentVariable("BTCX__Electrum__MaxRetries", oldElectrumRetries);
        }
    }

    private static async Task ExerciseRegtestPaymentAsync(
        BTCPayServer.Client.BTCPayServerClient client,
        string invoiceId,
        string destination,
        string cookiePath,
        RuntimeElectrumMock electrum)
    {
        var initialHeight = (await CallNodeRpcAsync(cookiePath, null, "getblockcount", [])).GetInt32();
        var txId = (await CallNodeRpcAsync(cookiePath, "btcx-miner", "sendtoaddress", [destination, 37.5m])).GetString()!;
        electrum.SetHistory(txId, 0);

        BTCPayServer.Client.Models.InvoiceData? invoice = null;
        await RetryUntilAsync(async () =>
        {
            invoice = await client.GetInvoice(invoiceId, includePaymentMethods: true, token: TestContext.Current.CancellationToken);
            return invoice.PaymentMethods.Single().Payments?.Any(payment => payment.Status == BTCPayServer.Client.Models.InvoicePaymentMethodDataModel.Payment.PaymentStatus.Processing) == true;
        }, "BTCPay did not persist the real regtest mempool payment.");
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        invoice = await client.GetInvoice(invoiceId, includePaymentMethods: true, token: TestContext.Current.CancellationToken);
        Assert.Single(invoice.PaymentMethods.Single().Payments!);

        for (var block = 0; block < 6; block++)
        {
            var chain = await CallNodeRpcAsync(cookiePath, null, "getblockchaininfo", []);
            var bestHash = chain.GetProperty("bestblockhash").GetString()!;
            var header = await CallNodeRpcAsync(cookiePath, null, "getblockheader", [bestHash, true]);
            await CallNodeRpcAsync(cookiePath, null, "setmocktime", [header.GetProperty("time").GetInt64() + 121]);
            await CallNodeRpcAsync(cookiePath, null, "generatetoaddress", [1, "rpocx1qlkmnuy53wmmj5wmfct868pq4mhjxv5kshk650y", 1_000_000]);
        }
        electrum.SetHistory(txId, initialHeight + 1);

        await RetryUntilAsync(async () =>
        {
            invoice = await client.GetInvoice(invoiceId, includePaymentMethods: true, token: TestContext.Current.CancellationToken);
            return invoice.Status == BTCPayServer.Client.Models.InvoiceStatus.Settled &&
                   invoice.PaymentMethods.Single().Payments?.Any(payment => payment.Status == BTCPayServer.Client.Models.InvoicePaymentMethodDataModel.Payment.PaymentStatus.Settled) == true;
        }, "BTCPay did not settle the six-confirmation regtest payment.");
        Assert.Equal(7.50m, invoice!.PaidAmount);
        Assert.Equal(37.5m, invoice.PaymentMethods.Single().PaymentMethodPaid);

        var transaction = await CallNodeRpcAsync(cookiePath, null, "getrawtransaction", [txId, 1]);
        var containingBlock = transaction.GetProperty("blockhash").GetString()!;
        await CallNodeRpcAsync(cookiePath, null, "invalidateblock", [containingBlock]);
        electrum.SetHistory(txId, 0);
        await RetryUntilAsync(async () =>
        {
            invoice = await client.GetInvoice(invoiceId, includePaymentMethods: true, token: TestContext.Current.CancellationToken);
            return invoice.PaymentMethods.Single().Payments?.Single().Status == BTCPayServer.Client.Models.InvoicePaymentMethodDataModel.Payment.PaymentStatus.Processing;
        }, "The real regtest reorg did not reverse the settled BTCX payment to processing.");
        await CallNodeRpcAsync(cookiePath, null, "reconsiderblock", [containingBlock]);
        electrum.SetHistory(txId, initialHeight + 1);
        await RetryUntilAsync(async () =>
        {
            invoice = await client.GetInvoice(invoiceId, includePaymentMethods: true, token: TestContext.Current.CancellationToken);
            return invoice.Status == BTCPayServer.Client.Models.InvoiceStatus.Settled &&
                   invoice.PaymentMethods.Single().Payments?.Single().Status == BTCPayServer.Client.Models.InvoicePaymentMethodDataModel.Payment.PaymentStatus.Settled;
        }, "The reconfirmed regtest transaction did not return to settled.");
    }

    private static async Task<JsonElement> CallNodeRpcAsync(string cookiePath, string? wallet, string method, object?[] parameters)
    {
        var cookie = (await File.ReadAllTextAsync(cookiePath, TestContext.Current.CancellationToken)).Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, wallet is null
            ? "http://127.0.0.1:18443/"
            : $"http://127.0.0.1:18443/wallet/{Uri.EscapeDataString(wallet)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(cookie)));
        request.Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json");
        using var httpClient = new HttpClient();
        using var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        if (body.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException($"Regtest RPC '{method}' failed: {error}");
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task RetryUntilAsync(Func<Task<bool>> condition, string error)
    {
        var until = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < until)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        }
        throw new TimeoutException(error);
    }

    private sealed class RuntimeWalletRpcMock : IAsyncDisposable
    {
        private const string GenesisHash = "2a98a52253aeff06093948b00568d380b7634621bc606403127973c9acbbfde0";
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Dictionary<string, string> _allocatedLabels = new(StringComparer.Ordinal);
        private Task? _serveTask;
        private int _port;

        public string Endpoint => $"http://127.0.0.1:{_port}/";

        public async Task StartAsync()
        {
            using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            _port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add(Endpoint);
            _listener.Start();
            _serveTask = ServeAsync(_stop.Token);
            await Task.Yield();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }

                using var request = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellationToken);
                var root = request.RootElement;
                var id = root.GetProperty("id").GetRawText();
                var method = root.GetProperty("method").GetString();
                var parameters = root.GetProperty("params");
                object result = method switch
                {
                    "getblockchaininfo" => new { chain = "regtest", blocks = 0, headers = 0, bestblockhash = GenesisHash, initialblockdownload = false, pruned = false },
                    "getblockhash" => GenesisHash,
                    "getaddressesbylabel" => GetAddresses(parameters[0].GetString()!),
                    "getnewaddress" => Allocate(parameters[0].GetString()!),
                    _ => throw new InvalidOperationException($"Unexpected runtime smoke RPC method '{method}'.")
                };
                var payload = Encoding.UTF8.GetBytes($"{{\"result\":{JsonSerializer.Serialize(result)},\"error\":null,\"id\":{id}}}");
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, cancellationToken);
                context.Response.Close();
            }
        }

        private object GetAddresses(string label) => _allocatedLabels.TryGetValue(label, out var address)
            ? new Dictionary<string, object> { [address] = new { purpose = "receive" } }
            : new Dictionary<string, object>();

        private string Allocate(string label)
        {
            var scriptBytes = new byte[22];
            scriptBytes[0] = 0;
            scriptBytes[1] = 20;
            Array.Fill(scriptBytes, (byte)(_allocatedLabels.Count + 1), 2, 20);
            var address = BtcxAddress.FromScriptPubKey(new Script(scriptBytes), BtcxNetworkId.Regtest);
            _allocatedLabels[label] = address;
            return address;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            if (_serveTask is not null)
                await _serveTask;
            _stop.Dispose();
        }
    }

    private sealed class RuntimeElectrumMock : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private Task? _serveTask;
        private string? _txId;
        private int _height;

        public string Endpoint { get; private set; } = string.Empty;

        public Task StartAsync()
        {
            _listener.Start();
            Endpoint = $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _serveTask = ServeAsync(_stop.Token);
            return Task.CompletedTask;
        }

        public void SetHistory(string txId, int height)
        {
            Volatile.Write(ref _height, height);
            Volatile.Write(ref _txId, txId);
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                        continue;
                    using var request = JsonDocument.Parse(line);
                    var id = request.RootElement.GetProperty("id").Clone();
                    var txId = Volatile.Read(ref _txId);
                    object history = txId is null ? Array.Empty<object>() : new object[] { new { tx_hash = txId, height = Volatile.Read(ref _height) } };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = history, error = (object?)null }));
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
            return ValueTask.CompletedTask;
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
