using System.Globalization;
using System.Text.Json;
using BTCPayServer.Plugins.BTCX.Rpc;
using NBitcoin;

namespace BTCPayServer.Plugins.BTCX.Discovery;

public sealed record BtcxObservedOutput(
    string TransactionId,
    uint OutputIndex,
    long AmountAtomicUnits,
    string Network,
    string ScriptPubKeyHex,
    string? BlockHash,
    int? BlockHeight,
    int Confirmations,
    bool IsMempool)
{
    public string PaymentId => $"{Network}-{TransactionId}-{OutputIndex.ToString(CultureInfo.InvariantCulture)}";
}

public interface IBtcxObservedPaymentSink
{
    Task<bool> RecordAsync(string invoiceId, BtcxObservedOutput output, CancellationToken cancellationToken = default);
}

/// <summary>
/// Electrum is only a discovery source. Every tx is fetched from the configured
/// PoCX node and every candidate output is matched by exact script bytes.
/// </summary>
public sealed class BtcxPaymentReconciler(
    IBtcxAddressHistoryClient historyClient,
    IBtcxChainReader rpcClient,
    IBtcxObservedPaymentSink sink)
{
    public async Task ReconcileInvoiceAsync(
        string invoiceId,
        BtcxNetworkId network,
        Script expectedScript,
        CancellationToken cancellationToken = default)
    {
        var chainName = BtcxNetworkParameters.For(network).ChainName;
        var scriptHash = BtcxElectrumScriptHash.FromScriptPubKey(expectedScript.ToBytes());
        var history = await historyClient.GetHistoryAsync(scriptHash, cancellationToken).ConfigureAwait(false);
        var blockHashes = new Dictionary<int, string>();

        foreach (var entry in history.GroupBy(item => item.TxId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? blockHash = null;
            if (entry.Height > 0)
            {
                if (!blockHashes.TryGetValue(entry.Height, out blockHash))
                {
                    blockHash = await rpcClient.GetBlockHashAsync(entry.Height, cancellationToken).ConfigureAwait(false);
                    blockHashes.Add(entry.Height, blockHash);
                }
            }

            BtcxDecodedTransaction transaction;
            try
            {
                transaction = await rpcClient.GetDecodedTransactionAsync(entry.TxId, blockHash, cancellationToken).ConfigureAwait(false);
            }
            catch (BtcxRpcException ex) when (ex.RpcCode == -5)
            {
                // The index can lag a block/mempool transition. Recheck next poll.
                continue;
            }

            if (!string.Equals(transaction.TxId, entry.TxId, StringComparison.OrdinalIgnoreCase))
                throw new BtcxRpcProtocolException("PoCX node returned a transaction with an unexpected transaction id.");
            if (blockHash is not null &&
                (transaction.InActiveChain is false || transaction.Confirmations is not > 0 ||
                 !string.Equals(transaction.BlockHash, blockHash, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var output in MatchOutputs(transaction, expectedScript))
            {
                var confirmed = transaction.BlockHash is not null && transaction.Confirmations is > 0 && transaction.InActiveChain is not false;
                _ = await sink.RecordAsync(invoiceId, output with
                {
                    Network = chainName,
                    BlockHash = confirmed ? transaction.BlockHash : null,
                    BlockHeight = confirmed ? entry.Height : null,
                    Confirmations = confirmed ? transaction.Confirmations!.Value : 0,
                    IsMempool = !confirmed
                }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static IReadOnlyList<BtcxObservedOutput> MatchOutputs(BtcxDecodedTransaction transaction, Script expectedScript)
    {
        if (transaction.Outputs.ValueKind != JsonValueKind.Array)
            throw new BtcxRpcProtocolException("PoCX node returned a transaction without a decoded output array.");
        var expectedBytes = expectedScript.ToBytes();
        var matches = new List<BtcxObservedOutput>();
        foreach (var output in transaction.Outputs.EnumerateArray())
        {
            try
            {
                var value = output.GetProperty("value").GetDecimal();
                var amount = BtcxAmount.FromDecimal(value);
                var index = output.GetProperty("n").GetUInt32();
                var scriptHex = output.GetProperty("scriptPubKey").GetProperty("hex").GetString()
                    ?? throw new JsonException();
                var scriptBytes = Convert.FromHexString(scriptHex);
                if (amount.AtomicUnits > 0 && scriptBytes.AsSpan().SequenceEqual(expectedBytes))
                    matches.Add(new BtcxObservedOutput(transaction.TxId, index, amount.AtomicUnits, "", Convert.ToHexString(scriptBytes).ToLowerInvariant(),
                        transaction.BlockHash, null, transaction.Confirmations ?? 0, transaction.BlockHash is null));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException or InvalidOperationException)
            {
                throw new BtcxRpcProtocolException("PoCX node returned a transaction output with an invalid BTCX amount or script.");
            }
        }
        return matches;
    }
}
