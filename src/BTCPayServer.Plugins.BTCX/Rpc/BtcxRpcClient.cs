using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BTCPayServer.Plugins.BTCX.Rpc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BTCX.Rpc;

public sealed class BtcxRpcClient : IBtcxRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly BtcxRpcOptions _options;
    private readonly Uri _endpoint;
    private long _requestId;

    public BtcxRpcClient(HttpClient httpClient, IOptions<BtcxRpcOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _endpoint = _options.Validate();
    }

    public Task<BtcxBlockchainInfo> GetBlockchainInfoAsync(CancellationToken cancellationToken = default) =>
        CallAsync<BtcxBlockchainInfo>("getblockchaininfo", [], null, cancellationToken);

    public Task<BtcxNetworkInfo> GetNetworkInfoAsync(CancellationToken cancellationToken = default) =>
        CallAsync<BtcxNetworkInfo>("getnetworkinfo", [], null, cancellationToken);

    public Task<string> GetBestBlockHashAsync(CancellationToken cancellationToken = default) =>
        CallAsync<string>("getbestblockhash", [], null, cancellationToken);

    public Task<string> GetBlockHashAsync(int height, CancellationToken cancellationToken = default)
    {
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        return CallAsync<string>("getblockhash", [height], null, cancellationToken);
    }

    public Task<int> GetBlockCountAsync(CancellationToken cancellationToken = default) =>
        CallAsync<int>("getblockcount", [], null, cancellationToken);

    public Task<BtcxBlockInfo> GetBlockAsync(string blockHash, int verbosity = 2, CancellationToken cancellationToken = default)
    {
        ValidateHash(blockHash, nameof(blockHash));
        if (verbosity is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(verbosity), "Use verbosity 1 (txids) or 2 (decoded transactions).");
        return CallAsync<BtcxBlockInfo>("getblock", [blockHash, verbosity], null, cancellationToken);
    }

    public Task<string> GetRawBlockAsync(string blockHash, CancellationToken cancellationToken = default)
    {
        ValidateHash(blockHash, nameof(blockHash));
        return CallAsync<string>("getblock", [blockHash, 0], null, cancellationToken);
    }

    public Task<BtcxDecodedTransaction> GetDecodedTransactionAsync(string txId, string? blockHash = null, CancellationToken cancellationToken = default)
    {
        ValidateHash(txId, nameof(txId));
        if (blockHash is not null)
            ValidateHash(blockHash, nameof(blockHash));
        object?[] parameters = blockHash is null ? [txId, 1] : [txId, 1, blockHash];
        return CallAsync<BtcxDecodedTransaction>("getrawtransaction", parameters, null, cancellationToken);
    }

    public Task<string> GetRawTransactionAsync(string txId, string? blockHash = null, CancellationToken cancellationToken = default)
    {
        ValidateHash(txId, nameof(txId));
        if (blockHash is not null)
            ValidateHash(blockHash, nameof(blockHash));
        object?[] parameters = blockHash is null ? [txId, 0] : [txId, 0, blockHash];
        return CallAsync<string>("getrawtransaction", parameters, null, cancellationToken);
    }

    /// <summary>
    /// Calls the wallet-scoped gettransaction RPC. The explicit wallet route is
    /// required; root/default-wallet discovery is intentionally not used.
    /// </summary>
    public Task<BtcxWalletTransaction> GetTransactionAsync(string txId, string walletName, bool includeDecoded = true, CancellationToken cancellationToken = default)
    {
        ValidateHash(txId, nameof(txId));
        if (string.IsNullOrWhiteSpace(walletName) || walletName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new ArgumentException("Wallet name must be an explicit simple path segment.", nameof(walletName));
        return CallAsync<BtcxWalletTransaction>("gettransaction", [txId, false, includeDecoded],
            "wallet/" + Uri.EscapeDataString(walletName), cancellationToken);
    }

    public Task<string> GetNewAddressAsync(string walletName, string label, string addressType = "bech32", CancellationToken cancellationToken = default)
    {
        ValidateWalletRoute(walletName);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (addressType is not "legacy" and not "p2sh-segwit" and not "bech32" and not "bech32m")
            throw new ArgumentException("Unsupported BTCX wallet address type.", nameof(addressType));
        // This mutates wallet state. The caller recovers an allocation by label;
        // blindly retrying after a timeout could create an extra address.
        return CallAsync<string>("getnewaddress", [label, addressType], "wallet/" + Uri.EscapeDataString(walletName), cancellationToken, retrySafe: false);
    }

    public Task<IReadOnlyDictionary<string, BtcxWalletAddressInfo>> GetAddressesByLabelAsync(
        string walletName, string label, CancellationToken cancellationToken = default)
    {
        ValidateWalletRoute(walletName);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return CallAsync<IReadOnlyDictionary<string, BtcxWalletAddressInfo>>(
            "getaddressesbylabel", [label], "wallet/" + Uri.EscapeDataString(walletName), cancellationToken);
    }

    public async Task<BtcxTransactionConfirmationInfo> GetTransactionConfirmationsAsync(
        string txId, string? blockHash = null, CancellationToken cancellationToken = default)
    {
        var transaction = await GetDecodedTransactionAsync(txId, blockHash, cancellationToken).ConfigureAwait(false);
        var confirmations = transaction.Confirmations ?? 0;
        return new BtcxTransactionConfirmationInfo(
            transaction.TxId,
            confirmations,
            transaction.BlockHash,
            transaction.InActiveChain,
            IsMempoolCandidate: transaction.BlockHash is null && transaction.Confirmations is null);
    }

    public async Task<BtcxBlockchainInfo> VerifyNetworkAsync(BtcxNetworkId expectedNetwork, CancellationToken cancellationToken = default)
    {
        var info = await GetBlockchainInfoAsync(cancellationToken).ConfigureAwait(false);
        var expected = BtcxNetworkParameters.For(expectedNetwork).ChainName;
        if (!string.Equals(info.Chain, expected, StringComparison.Ordinal))
            throw new BtcxRpcException($"Connected BTCX node reports chain '{info.Chain}', expected '{expected}'.", "getblockchaininfo");
        var genesis = await GetBlockHashAsync(0, cancellationToken).ConfigureAwait(false);
        var expectedGenesis = BtcxNetworkParameters.For(expectedNetwork).GenesisHash;
        if (!string.Equals(genesis, expectedGenesis, StringComparison.OrdinalIgnoreCase))
            throw new BtcxRpcException("Connected BTCX node has an unexpected genesis block.", "getblockhash");
        return info;
    }

    private async Task<T> CallAsync<T>(string method, object?[] parameters, string? relativePath, CancellationToken cancellationToken, bool retrySafe = true)
    {
        var uri = relativePath is null ? _endpoint : new Uri(_endpoint, relativePath);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var requestId = Interlocked.Increment(ref _requestId);
                using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Headers.Authorization = await GetAuthorizationAsync(cancellationToken).ConfigureAwait(false);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(JsonSerializer.Serialize(new RpcRequest(
                    "2.0", requestId, method, parameters), JsonOptions),
                    Encoding.UTF8, "application/json");

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                    .ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new BtcxRpcAuthenticationException();

                var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
                RpcEnvelope envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<RpcEnvelope>(body, JsonOptions)
                        ?? throw new JsonException();
                }
                catch (JsonException)
                {
                    if (retrySafe && (int)response.StatusCode >= 500 && attempt < _options.MaxRetries)
                    {
                        await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new BtcxRpcProtocolException("BTCX node returned a malformed JSON-RPC response.");
                }

                if (envelope.Id is not null &&
                    (envelope.Id.Value.ValueKind != JsonValueKind.Number ||
                     !envelope.Id.Value.TryGetInt64(out var responseId) || responseId != requestId))
                    throw new BtcxRpcProtocolException("BTCX node returned a mismatched JSON-RPC request id.");

                if (envelope.Error is { } rpcError)
                {
                    if (retrySafe && rpcError.Code == -28 && attempt < _options.MaxRetries)
                    {
                        await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new BtcxRpcException($"BTCX node RPC method '{method}' failed (code {rpcError.Code}).", method, rpcError.Code,
                        (int)response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (retrySafe && (int)response.StatusCode >= 500 && attempt < _options.MaxRetries)
                    {
                        await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new BtcxRpcException($"BTCX node RPC method '{method}' returned HTTP {(int)response.StatusCode}.", method,
                        httpStatus: (int)response.StatusCode);
                }

                if (envelope.Result is not { } result)
                    throw new BtcxRpcProtocolException("BTCX node JSON-RPC response has neither a result nor an error.");
                try
                {
                    return result.Deserialize<T>(JsonOptions)
                        ?? throw new JsonException();
                }
                catch (JsonException)
                {
                    throw new BtcxRpcProtocolException($"BTCX node RPC method '{method}' returned a result with an invalid schema.");
                }
            }
            catch (BtcxRpcAuthenticationException)
            {
                throw;
            }
            catch (BtcxRpcException)
            {
                throw;
            }
            catch (BtcxRpcProtocolException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (!retrySafe || attempt >= _options.MaxRetries)
                    throw new BtcxRpcUnavailableException($"BTCX node RPC method '{method}' timed out.", method);
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                if (!retrySafe || attempt >= _options.MaxRetries)
                    throw new BtcxRpcUnavailableException($"BTCX node RPC method '{method}' is unavailable.", method);
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken cancellationToken)
    {
        string username;
        string password;
        if (!string.IsNullOrWhiteSpace(_options.CookieFilePath))
        {
            string cookie;
            try
            {
                cookie = (await File.ReadAllTextAsync(_options.CookieFilePath, cancellationToken).ConfigureAwait(false)).Trim();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BtcxRpcUnavailableException("BTCX node RPC cookie is unavailable.", "authentication");
            }
            var separator = cookie.IndexOf(':');
            if (separator < 1 || separator == cookie.Length - 1)
                throw new BtcxRpcProtocolException("BTCX node RPC cookie has an invalid format.");
            username = cookie[..separator];
            password = cookie[(separator + 1)..];
        }
        else
        {
            username = _options.Username!;
            password = _options.Password!;
        }

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken) =>
        _options.RetryDelayMilliseconds == 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(_options.RetryDelayMilliseconds * (attempt + 1)), cancellationToken);

    private static void ValidateHash(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("BTCX transaction and block hashes must be 64 hexadecimal characters.", parameterName);
    }

    private static void ValidateWalletRoute(string walletName)
    {
        if (string.IsNullOrWhiteSpace(walletName) || walletName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new ArgumentException("Wallet name must be an explicit simple path segment.", nameof(walletName));
    }

    private sealed record RpcRequest(
        [property: JsonPropertyName("jsonrpc")] string JsonRpc,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] object?[] Parameters);

    private sealed record RpcEnvelope(
        [property: JsonPropertyName("result")] JsonElement? Result,
        [property: JsonPropertyName("error")] RpcError? Error,
        [property: JsonPropertyName("id")] JsonElement? Id);

    private sealed record RpcError(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message);
}

/// <summary>Restricts node RPC sockets to loopback/private/link-local IP ranges and disables proxies/redirects.</summary>
public static class BtcxRpcHttpHandler
{
    public static SocketsHttpHandler Create()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectPrivateAsync
        };
    }

    private static async ValueTask<Stream> ConnectPrivateAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            throw new HttpRequestException("BTCX node RPC host could not be resolved.");
        }

        foreach (var address in addresses.Where(BtcxPrivateEndpoint.IsPrivateOrLoopback))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }
        throw new HttpRequestException("BTCX node RPC connections to non-private addresses are rejected.");
    }
}
