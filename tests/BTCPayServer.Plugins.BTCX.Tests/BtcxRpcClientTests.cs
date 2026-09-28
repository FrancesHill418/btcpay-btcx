using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.BTCX.Rpc;
using Microsoft.Extensions.Options;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxRpcClientTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Successful_connection_uses_basic_auth_and_json_rpc()
    {
        var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://127.0.0.1:8332/", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("rpcuser:s3cret")),
                request.Headers.Authorization!.ToString());
            return Task.FromResult(JsonResponse(request, "\"" + Hash + "\""));
        });
        var client = CreateClient(handler);

        Assert.Equal(Hash, await client.GetBestBlockHashAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("getbestblockhash", handler.LastMethod);
        Assert.Equal(0, handler.LastParameters!.Value.GetArrayLength());
    }

    [Fact]
    public async Task Authentication_failure_is_not_retried()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var client = CreateClient(handler, retries: 3);
        await Assert.ThrowsAsync<BtcxRpcAuthenticationException>(() => client.GetBlockCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Wallet_address_generation_is_not_retried_after_ambiguous_server_failure()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("wallet may already have generated an address")
        }));
        var client = CreateClient(handler, retries: 3, retryDelay: 0);
        await Assert.ThrowsAsync<BtcxRpcProtocolException>(() => client.GetNewAddressAsync(
            "receive-only", "btcx-invoice-test", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Getblockchaininfo_reads_PoCX_chain_fields()
    {
        var handler = JsonHandler("{\"chain\":\"regtest\",\"blocks\":42,\"headers\":42,\"bestblockhash\":\"" + Hash + "\",\"base_target\":43980465111,\"generation_signature\":\"abcd\",\"initialblockdownload\":false,\"pruned\":false}");
        var info = await CreateClient(handler).GetBlockchainInfoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("regtest", info.Chain);
        Assert.Equal(42, info.Blocks);
        Assert.Equal(43980465111UL, info.BaseTarget);
        Assert.Equal("abcd", info.GenerationSignature);
    }

    [Fact]
    public async Task Network_verification_checks_the_genesis_hash_not_only_the_chain_name()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(
            handlerMethod(request) == "getblockchaininfo"
                ? JsonResponse(request, "{\"chain\":\"regtest\",\"blocks\":0,\"headers\":0,\"bestblockhash\":\"" + Hash + "\"}")
                : JsonResponse(request, "\"" + Hash + "\"")));
        var client = CreateClient(handler);
        var error = await Assert.ThrowsAsync<BtcxRpcException>(() => client.VerifyNetworkAsync(BtcxNetworkId.Regtest, TestContext.Current.CancellationToken));
        Assert.Equal("getblockhash", error.Method);
        Assert.Equal(2, handler.CallCount);

        static string handlerMethod(HttpRequestMessage request)
        {
            using var document = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return document.RootElement.GetProperty("method").GetString()!;
        }
    }

    [Fact]
    public async Task Getnetworkinfo_reads_network_fields()
    {
        var handler = JsonHandler("{\"version\":300000,\"subversion\":\"/Bitcoin-PoCX:30.0/\",\"protocolversion\":70016,\"localservices\":\"0000000000000409\",\"localrelay\":true,\"connections\":4,\"networkactive\":true,\"relayfee\":0.000001,\"incrementalfee\":0.00001,\"warnings\":[]}");
        var info = await CreateClient(handler).GetNetworkInfoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(300000, info.Version);
        Assert.Equal("/Bitcoin-PoCX:30.0/", info.Subversion);
        Assert.Equal(4, info.Connections);
        Assert.True(info.NetworkActive);
    }

    [Fact]
    public async Task Getbestblockhash_returns_hash()
    {
        Assert.Equal(Hash, await CreateClient(JsonHandler("\"" + Hash + "\"")).GetBestBlockHashAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Getblockcount_returns_validated_chain_height()
    {
        Assert.Equal(0, await CreateClient(JsonHandler("0")).GetBlockCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Getblock_decodes_core_and_PoCX_specific_fields()
    {
        var json = "{\"hash\":\"" + Hash + "\",\"confirmations\":3,\"height\":2,\"version\":1,\"merkleroot\":\"" + Hash + "\",\"time\":1296688602,\"mediantime\":1296688602,\"nTx\":1,\"tx\":[{\"txid\":\"" + Hash + "\"}],\"base_target\":1234,\"generation_signature\":\"deadbeef\",\"pocx_proof\":{\"quality\":1}}";
        var handler = JsonHandler(json);
        var block = await CreateClient(handler).GetBlockAsync(Hash, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, block.Height);
        Assert.Equal(3, block.Confirmations);
        Assert.Equal("1234", block.Extensions!["base_target"].GetRawText());
        Assert.Equal(2, handler.LastParameters!.Value.GetArrayLength());
        Assert.Equal(2, handler.LastParameters.Value[1].GetInt32());
    }

    [Fact]
    public async Task Gettransaction_uses_explicit_wallet_route_and_decoded_shape()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(JsonResponse(request,
            "{\"txid\":\"" + Hash + "\",\"amount\":1.25,\"confirmations\":2,\"hex\":\"00\",\"decoded\":{\"txid\":\"" + Hash + "\",\"version\":2,\"size\":10,\"vsize\":10,\"weight\":40,\"locktime\":0}}")));
        var tx = await CreateClient(handler).GetTransactionAsync(Hash, "receive-only", includeDecoded: true,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("/wallet/receive-only", handler.LastPath);
        Assert.Equal(Hash, tx.TxId);
        Assert.Equal(2, tx.Confirmations);
        Assert.Equal(Hash, tx.Decoded!.TxId);
        Assert.Equal("gettransaction", handler.LastMethod);
    }

    [Fact]
    public async Task Getrawtransaction_supports_raw_lookup_with_blockhash()
    {
        var handler = JsonHandler("\"00aabb\"");
        var raw = await CreateClient(handler).GetRawTransactionAsync(Hash, Hash, TestContext.Current.CancellationToken);
        Assert.Equal("00aabb", raw);
        Assert.Equal("getrawtransaction", handler.LastMethod);
        Assert.Equal(Hash, handler.LastParameters!.Value[2].GetString());
    }

    [Fact]
    public async Task Decoded_transaction_lookup_uses_verbosity_one()
    {
        var handler = JsonHandler("{\"txid\":\"" + Hash + "\",\"version\":2,\"size\":10,\"vsize\":10,\"weight\":40,\"locktime\":0,\"hex\":\"00\",\"confirmations\":0,\"vin\":[],\"vout\":[]}");
        var tx = await CreateClient(handler).GetDecodedTransactionAsync(Hash, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Hash, tx.TxId);
        Assert.Equal(1, handler.LastParameters!.Value[1].GetInt32());
    }

    [Fact]
    public async Task Confirmation_lookup_preserves_mempool_zero_confirmation_semantics()
    {
        var handler = JsonHandler("{\"txid\":\"" + Hash + "\",\"version\":2,\"size\":10,\"vsize\":10,\"weight\":40,\"locktime\":0,\"hex\":\"00\",\"vin\":[],\"vout\":[]}");
        var info = await CreateClient(handler).GetTransactionConfirmationsAsync(Hash, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, info.Confirmations);
        Assert.Null(info.BlockHash);
        Assert.True(info.IsMempoolCandidate);
    }

    [Fact]
    public async Task Malformed_response_is_rejected_without_echoing_body()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("this body must not be copied to exception text")
        }));
        var error = await Assert.ThrowsAsync<BtcxRpcProtocolException>(() => CreateClient(handler).GetBlockCountAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("this body", error.Message);
    }

    [Fact]
    public async Task Timeout_retries_then_reports_timeout()
    {
        var handler = new FakeHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler, timeoutSeconds: 1, retries: 1, retryDelay: 0);
        await Assert.ThrowsAsync<BtcxRpcUnavailableException>(() => client.GetBlockCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Unavailable_node_retries_transport_errors()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("private transport detail"));
        var client = CreateClient(handler, retries: 2, retryDelay: 0);
        var error = await Assert.ThrowsAsync<BtcxRpcUnavailableException>(() => client.GetBlockCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, handler.CallCount);
        Assert.DoesNotContain("private transport detail", error.Message);
    }

    [Fact]
    public async Task Transient_HTTP_503_retries_then_recovers()
    {
        var calls = 0;
        var handler = new FakeHandler((request, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("temporary")
                });
            return Task.FromResult(JsonResponse(request, "7"));
        });
        Assert.Equal(7, await CreateClient(handler, retries: 1, retryDelay: 0).GetBlockCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried_or_rewritten_as_timeout()
    {
        var handler = new FakeHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(handler, retries: 4, retryDelay: 0).GetBlockCountAsync(cancellation.Token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Cookie_authentication_is_read_per_request()
    {
        var cookiePath = Path.Combine(Path.GetTempPath(), "btcx-rpc-cookie-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(cookiePath, "__cookie__:cookie-secret", TestContext.Current.CancellationToken);
            var handler = new FakeHandler((request, _) =>
            {
                Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("__cookie__:cookie-secret")),
                    request.Headers.Authorization!.ToString());
                return Task.FromResult(JsonResponse(request, "1"));
            });
            Assert.Equal(1, await CreateClient(handler, cookieFilePath: cookiePath).GetBlockCountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(cookiePath);
        }
    }

    [Fact]
    public void Public_RPC_endpoint_is_rejected()
    {
        var options = CreateOptions();
        options.Endpoint = "https://8.8.8.8:8332/";
        Assert.Throws<OptionsValidationException>(() => options.Validate());
    }

    private static BtcxRpcClient CreateClient(
        FakeHandler handler, int timeoutSeconds = 10, int retries = 0, int retryDelay = 0, string? cookieFilePath = null)
    {
        var options = CreateOptions();
        options.TimeoutSeconds = timeoutSeconds;
        options.MaxRetries = retries;
        options.RetryDelayMilliseconds = retryDelay;
        options.CookieFilePath = cookieFilePath;
        if (cookieFilePath is not null)
        {
            options.Username = null;
            options.Password = null;
        }
        return new BtcxRpcClient(new HttpClient(handler), Options.Create(options));
    }

    private static BtcxRpcOptions CreateOptions() => new()
    {
        Endpoint = "http://127.0.0.1:8332/",
        Username = "rpcuser",
        Password = "s3cret",
        TimeoutSeconds = 10,
        MaxRetries = 0,
        RetryDelayMilliseconds = 0
    };

    private static FakeHandler JsonHandler(string result) => new((request, _) => Task.FromResult(JsonResponse(request, result)));

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string result, HttpStatusCode status = HttpStatusCode.OK)
    {
        var requestJson = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var id = requestJson.RootElement.GetProperty("id").GetInt64();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"result\":" + result + ",\"error\":null,\"id\":" + id + "}", Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public JsonElement? LastParameters { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPath = request.RequestUri!.AbsolutePath;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            LastMethod = json.RootElement.GetProperty("method").GetString();
            LastParameters = json.RootElement.GetProperty("params").Clone();
            return await send(request, cancellationToken);
        }
    }
}
