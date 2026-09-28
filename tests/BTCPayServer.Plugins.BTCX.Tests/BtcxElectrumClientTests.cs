using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BTCPayServer.Plugins.BTCX.Discovery;
using Microsoft.Extensions.Options;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxElectrumClientTests
{
    private const string ScriptHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string TxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Electrum_script_hash_uses_reversed_sha256_bytes()
    {
        Assert.Equal("55b852781b9995a44c939b64e441ae2724b96f99c8f4fb9a141cfc9842c4b0e3",
            BtcxElectrumScriptHash.FromScriptPubKey([]));
    }

    [Fact]
    public async Task Queries_Electrum_history_using_expected_JSON_RPC_and_parses_confirmation_height()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            using var reader = new StreamReader(socket.GetStream());
            using var writer = new StreamWriter(socket.GetStream()) { AutoFlush = true };
            using var request = JsonDocument.Parse((await reader.ReadLineAsync(TestContext.Current.CancellationToken))!);
            Assert.Equal("blockchain.scripthash.get_history", request.RootElement.GetProperty("method").GetString());
            Assert.Equal(ScriptHash, request.RootElement.GetProperty("params")[0].GetString());
            var id = request.RootElement.GetProperty("id").GetInt64();
            var response = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":[{\"tx_hash\":\"" + TxId + "\",\"height\":0},{\"tx_hash\":\"" + TxId + "\",\"height\":7}]}";
            await writer.WriteLineAsync(response.AsMemory(), TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        try
        {
            var options = CreateOptions(port);
            var history = await new BtcxElectrumClient(options).GetHistoryAsync(ScriptHash, TestContext.Current.CancellationToken);
            Assert.Equal(2, history.Count);
            Assert.Equal(0, history[0].Height);
            Assert.Equal(7, history[1].Height);
            await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Malformed_history_entry_is_rejected_without_echoing_response()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ReplyOnceAsync(listener, "{\"tx_hash\":\"not-a-hash\",\"height\":1}");
        try
        {
            var error = await Assert.ThrowsAsync<BtcxElectrumProtocolException>(() =>
                new BtcxElectrumClient(CreateOptions(port)).GetHistoryAsync(ScriptHash, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("not-a-hash", error.Message);
            await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Electrum_unavailable_retries_then_returns_safe_error()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var client = new BtcxElectrumClient(CreateOptions(port, retries: 1));

        var error = await Assert.ThrowsAsync<BtcxElectrumUnavailableException>(() =>
            client.GetHistoryAsync(ScriptHash, TestContext.Current.CancellationToken));

        Assert.Contains("BTCX Electrum", error.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var server = Task.Run(async () =>
        {
            try
            {
                using var socket = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                using var reader = new StreamReader(socket.GetStream());
                _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }, TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new BtcxElectrumClient(CreateOptions(port, timeoutSeconds: 5)).GetHistoryAsync(ScriptHash, cancellation.Token));
            await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Public_electrum_endpoint_is_rejected()
    {
        var options = new BtcxElectrumOptions { Endpoint = "tcp://8.8.8.8:50001/" };
        Assert.Throws<OptionsValidationException>(() => options.Validate());
    }

    private static IOptions<BtcxElectrumOptions> CreateOptions(int port, int retries = 0, int timeoutSeconds = 2) =>
        Options.Create(new BtcxElectrumOptions
        {
            Enabled = true,
            Endpoint = $"tcp://127.0.0.1:{port}/",
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = retries,
            PollIntervalSeconds = 2
        });

    private static async Task ReplyOnceAsync(TcpListener listener, string result)
    {
        using var socket = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(socket.GetStream());
        using var writer = new StreamWriter(socket.GetStream()) { AutoFlush = true };
        using var request = JsonDocument.Parse((await reader.ReadLineAsync(TestContext.Current.CancellationToken))!);
        var id = request.RootElement.GetProperty("id").GetInt64();
        var response = "{\"id\":" + id + ",\"result\":[" + result + "],\"error\":null}";
        await writer.WriteLineAsync(response.AsMemory(), TestContext.Current.CancellationToken);
    }
}
