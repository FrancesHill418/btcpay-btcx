using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.BTCX.Rpc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BTCX.Discovery;

/// <summary>Minimal read-only Electrum JSON-RPC client for script history discovery.</summary>
public sealed class BtcxElectrumClient(IOptions<BtcxElectrumOptions> options) : IBtcxAddressHistoryClient
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private readonly BtcxElectrumOptions _options = options.Value;
    private long _requestId;

    public async Task<IReadOnlyList<BtcxAddressHistoryEntry>> GetHistoryAsync(string scriptHash, CancellationToken cancellationToken = default)
    {
        ValidateScriptHash(scriptHash);
        var endpoint = _options.Validate();
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                var response = await QueryAsync(endpoint, scriptHash, timeout.Token).ConfigureAwait(false);
                return ParseHistory(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (attempt >= _options.MaxRetries)
                    throw new BtcxElectrumUnavailableException("BTCX Electrum query timed out.");
                await RetryDelayAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (BtcxElectrumUnavailableException) when (attempt < _options.MaxRetries)
            {
                await RetryDelayAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (BtcxElectrumUnavailableException)
            {
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                if (attempt >= _options.MaxRetries)
                    throw new BtcxElectrumUnavailableException("BTCX Electrum index is unavailable.");
                await RetryDelayAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> QueryAsync(Uri endpoint, string scriptHash, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        foreach (var address in addresses.Where(BtcxPrivateEndpoint.IsPrivateOrLoopback))
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false);
                using var stream = new NetworkStream(socket, ownsSocket: false);
                var id = Interlocked.Increment(ref _requestId);
                var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "blockchain.scripthash.get_history", @params = new[] { scriptHash } }) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(request), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var line = await ReadResponseLineAsync(reader, cancellationToken).ConfigureAwait(false);
                if (line is null)
                    throw new BtcxElectrumProtocolException("BTCX Electrum returned an empty or oversized response.");
                return ValidateEnvelope(line, id);
            }
            catch (SocketException)
            {
                // Try another resolved private address. Public DNS results are never connected.
            }
        }
        throw new BtcxElectrumUnavailableException("BTCX Electrum cannot be reached on a loopback/private address.");
    }

    private static async Task<string?> ReadResponseLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return line.Length == 0 ? null : line.ToString();
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                    return line.ToString().TrimEnd('\r');
                line.Append(character);
                if (line.Length > MaximumResponseBytes)
                    throw new BtcxElectrumProtocolException("BTCX Electrum returned an empty or oversized response.");
            }
        }
    }

    private static string ValidateEnvelope(string line, long requestId)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var receivedId) || receivedId != requestId)
                throw new BtcxElectrumProtocolException("BTCX Electrum returned a mismatched JSON-RPC request id.");
            if (root.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null)
                throw new BtcxElectrumProtocolException("BTCX Electrum rejected the script history query.");
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                throw new BtcxElectrumProtocolException("BTCX Electrum returned an invalid script history schema.");
            return result.GetRawText();
        }
        catch (JsonException)
        {
            throw new BtcxElectrumProtocolException("BTCX Electrum returned malformed JSON.");
        }
    }

    private static IReadOnlyList<BtcxAddressHistoryEntry> ParseHistory(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateArray().Select(entry =>
            {
                var txId = entry.GetProperty("tx_hash").GetString() ?? throw new JsonException();
                ValidateScriptHash(txId);
                return new BtcxAddressHistoryEntry(txId, entry.GetProperty("height").GetInt32());
            }).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new BtcxElectrumProtocolException("BTCX Electrum returned a malformed transaction history entry.");
        }
    }

    private Task RetryDelayAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);

    private static void ValidateScriptHash(string scriptHash)
    {
        if (scriptHash.Length != 64 || scriptHash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Electrum script hashes and transaction ids must be 64 hexadecimal characters.", nameof(scriptHash));
    }
}

public sealed class BtcxElectrumProtocolException(string message) : Exception(message);
public sealed class BtcxElectrumUnavailableException(string message) : Exception(message);
