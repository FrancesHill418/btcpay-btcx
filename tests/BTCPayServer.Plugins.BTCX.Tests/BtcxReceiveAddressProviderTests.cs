using System.Net;
using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.BTCX.Rpc;
using BTCPayServer.Plugins.BTCX.Wallet;
using Microsoft.Extensions.Options;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxReceiveAddressProviderTests
{
    private static readonly string AddressOne = MakeAddress(0x11);
    private static readonly string AddressTwo = MakeAddress(0x22);

    [Fact]
    public async Task Allocates_unique_addresses_and_recovers_existing_label_after_provider_restart()
    {
        var wallet = new FakeWalletHandler("regtest");
        var first = CreateProvider(wallet);
        var invoiceA = await first.GetOrAllocateAsync("invoice-a", TestContext.Current.CancellationToken);
        var invoiceB = await first.GetOrAllocateAsync("invoice-b", TestContext.Current.CancellationToken);

        Assert.NotEqual(invoiceA.Address, invoiceB.Address);
        Assert.Equal("regtest", invoiceA.Network);
        Assert.StartsWith("btcx-script:", invoiceA.TrackingToken);
        Assert.Equal(2, wallet.Methods.Count(method => method == "getnewaddress"));

        var restartedProvider = CreateProvider(wallet);
        var recovered = await restartedProvider.GetOrAllocateAsync("invoice-a", TestContext.Current.CancellationToken);
        Assert.Equal(invoiceA.Address, recovered.Address);
        Assert.Equal(invoiceA.ScriptPubKey, recovered.ScriptPubKey);
        Assert.Equal(2, wallet.Methods.Count(method => method == "getnewaddress"));
    }

    [Fact]
    public async Task Rejects_wallet_address_with_invalid_checksum_or_network()
    {
        var wallet = new FakeWalletHandler("regtest", addressOverride: "rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzexx");
        var provider = CreateProvider(wallet);
        await Assert.ThrowsAsync<FormatException>(() => provider.GetOrAllocateAsync("invoice-invalid", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_valid_address_for_another_network()
    {
        const string testnetAddress = "tpocx1qcpueamxr0aa82t7dtvhzdksq59c993f90zw6pr";
        var wallet = new FakeWalletHandler("regtest", addressOverride: testnetAddress);
        var provider = CreateProvider(wallet);
        var error = await Assert.ThrowsAsync<FormatException>(() => provider.GetOrAllocateAsync("invoice-testnet-address", TestContext.Current.CancellationToken));
        Assert.Contains("not Regtest", error.Message);
    }

    [Fact]
    public async Task Rejects_node_on_wrong_network_before_allocating_address()
    {
        var wallet = new FakeWalletHandler("test");
        var provider = CreateProvider(wallet);
        await Assert.ThrowsAsync<BtcxRpcException>(() => provider.GetOrAllocateAsync("invoice-wrong-network", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("getnewaddress", wallet.Methods);
    }

    [Fact]
    public async Task Mainnet_is_disabled_without_explicit_configuration_gate()
    {
        var wallet = new FakeWalletHandler("main");
        var rpc = CreateProvider(wallet, network: "main");
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            rpc.GetOrAllocateAsync("invoice-mainnet", TestContext.Current.CancellationToken));
        Assert.Empty(wallet.Methods);
    }

    [Fact]
    public async Task Mainnet_address_allocation_requires_and_honors_explicit_configuration_gate()
    {
        var mainnetAddress = BtcxAddress.FromScriptPubKey(
            new NBitcoin.Script([0x00, 0x14, .. Enumerable.Repeat((byte)0x33, 20)]), BtcxNetworkId.Mainnet);
        var wallet = new FakeWalletHandler("main", addressOverride: mainnetAddress);
        var provider = CreateProvider(wallet, network: "main", allowMainnet: true);

        var receive = await provider.GetOrAllocateAsync("invoice-mainnet-gated", TestContext.Current.CancellationToken);

        Assert.Equal("main", receive.Network);
        Assert.Equal(mainnetAddress, receive.Address);
        Assert.Contains("getblockchaininfo", wallet.Methods);
        Assert.Contains("getnewaddress", wallet.Methods);
    }

    private static BtcxReceiveAddressProvider CreateProvider(FakeWalletHandler handler, string network = "regtest", bool allowMainnet = false)
    {
        var rpc = new BtcxRpcClient(new HttpClient(handler), Options.Create(new BtcxRpcOptions
        {
            Endpoint = "http://127.0.0.1:18443/", Username = "test", Password = "fake",
            TimeoutSeconds = 2, MaxRetries = 0, RetryDelayMilliseconds = 0
        }));
        return new BtcxReceiveAddressProvider(() => rpc, Options.Create(new BtcxWalletOptions
        {
            WalletName = "receive-only", Network = network, AllowMainnet = allowMainnet
        }));
    }

    private static string MakeAddress(byte value)
    {
        var program = Enumerable.Repeat(value, 20).ToArray();
        return BtcxAddress.FromScriptPubKey(new NBitcoin.Script([0x00, 0x14, .. program]), BtcxNetworkId.Regtest);
    }

    private sealed class FakeWalletHandler(string chain, string? addressOverride = null) : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _addresses = new(StringComparer.Ordinal);
        private int _addressIndex;
        public List<string> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            var id = root.GetProperty("id").GetInt64();
            var method = root.GetProperty("method").GetString()!;
            var args = root.GetProperty("params");
            Methods.Add(method);

            string? result = method switch
            {
                "getblockchaininfo" => "{\"chain\":\"" + chain + "\",\"blocks\":0,\"headers\":0,\"bestblockhash\":\"" + BtcxNetworkParameters.For(BtcxNetworkId.Regtest).GenesisHash + "\"}",
                "getblockhash" => "\"" + BtcxNetworkParameters.For(chain switch
                {
                    "main" => BtcxNetworkId.Mainnet,
                    "test" => BtcxNetworkId.Testnet,
                    _ => BtcxNetworkId.Regtest
                }).GenesisHash + "\"",
                "getaddressesbylabel" => ReadLabel(args[0].GetString()!),
                "getnewaddress" => CreateAddress(args[0].GetString()!),
                _ => throw new InvalidOperationException("Unexpected RPC method " + method)
            };
            if (result is null)
                return JsonRpc(request, id, "null", -11);
            return JsonRpc(request, id, result);
        }

        private string? ReadLabel(string label) => _addresses.TryGetValue(label, out var address)
            ? "{\"" + address + "\":{\"purpose\":\"receive\"}}"
            : null;

        private string CreateAddress(string label)
        {
            var address = addressOverride ?? (++_addressIndex == 1 ? AddressOne : AddressTwo);
            _addresses[label] = address;
            return JsonSerializer.Serialize(address);
        }

        private static HttpResponseMessage JsonRpc(HttpRequestMessage request, long id, string result, int? code = null)
        {
            var envelope = code is null
                ? "{\"result\":" + result + ",\"error\":null,\"id\":" + id + "}"
                : "{\"result\":null,\"error\":{\"code\":" + code + ",\"message\":\"not found\"},\"id\":" + id + "}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
    }
}
