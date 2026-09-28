using System.Text.Json;
using BTCPayServer.Plugins.BTCX.Discovery;
using BTCPayServer.Plugins.BTCX.Rpc;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxPaymentReconcilerTests
{
    private const string TxOne = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TxTwo = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string BlockHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private static readonly Script ReceiveScript = BtcxAddress.Parse("rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt", BtcxNetworkId.Regtest).ScriptPubKey;
    private static readonly string ReceiveScriptHex = Convert.ToHexString(ReceiveScript.ToBytes()).ToLowerInvariant();

    [Theory]
    [InlineData("37.5", "exact")]
    [InlineData("25", "underpayment")]
    [InlineData("40", "overpayment")]
    public async Task Exact_under_and_overpayment_outputs_are_recorded_as_atomic_units(string amount, string _)
    {
        var sink = new FakeSink();
        var reconciler = CreateReconciler(sink,
            [new BtcxAddressHistoryEntry(TxOne, 0)],
            [Transaction(TxOne, Output(0, amount, ReceiveScriptHex))]);

        await reconciler.ReconcileInvoiceAsync("invoice-one", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);

        var payment = Assert.Single(sink.Outputs.Values);
        Assert.Equal(BtcxAmount.FromDecimal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).AtomicUnits, payment.AmountAtomicUnits);
        Assert.True(payment.IsMempool);
        Assert.Equal(0, payment.Confirmations);
    }

    [Fact]
    public async Task Split_payment_and_multiple_matching_outputs_keep_distinct_outpoint_identity()
    {
        var sink = new FakeSink();
        var reconciler = CreateReconciler(sink,
            [new BtcxAddressHistoryEntry(TxOne, 0), new BtcxAddressHistoryEntry(TxTwo, 0)],
            [Transaction(TxOne, Output(0, "10", ReceiveScriptHex) + "," + Output(1, "12.5", ReceiveScriptHex)),
             Transaction(TxTwo, Output(0, "15", ReceiveScriptHex))]);

        await reconciler.ReconcileInvoiceAsync("invoice-split", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);

        Assert.Equal(3, sink.Outputs.Count);
        Assert.Equal(BtcxAmount.FromDecimal(37.5m).AtomicUnits, sink.Outputs.Values.Sum(payment => payment.AmountAtomicUnits));
        Assert.Equal(3, sink.Outputs.Keys.Distinct().Count());
    }

    [Fact]
    public async Task Duplicate_history_duplicate_poll_and_restart_are_idempotent_by_network_txid_vout()
    {
        var sink = new FakeSink();
        var history = new[] { new BtcxAddressHistoryEntry(TxOne, 0), new BtcxAddressHistoryEntry(TxOne, 0) };
        var transaction = Transaction(TxOne, Output(2, "1", ReceiveScriptHex));
        var rpc = new FakeChainReader([transaction]);

        await CreateReconciler(sink, history, rpc).ReconcileInvoiceAsync("invoice-duplicate", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);
        await CreateReconciler(sink, history, rpc).ReconcileInvoiceAsync("invoice-duplicate", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);

        var payment = Assert.Single(sink.Outputs.Values);
        Assert.Equal($"regtest-{TxOne}-2", payment.PaymentId);
    }

    [Fact]
    public async Task Confirmed_history_is_checked_against_node_canonical_hash()
    {
        var sink = new FakeSink();
        var tx = Transaction(TxOne, Output(0, "1", ReceiveScriptHex), BlockHash, 2, inActiveChain: true);
        var rpc = new FakeChainReader([tx]) { CanonicalBlockHash = BlockHash };
        var reconciler = CreateReconciler(sink, [new BtcxAddressHistoryEntry(TxOne, 10)], rpc);

        await reconciler.ReconcileInvoiceAsync("invoice-confirmed", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);

        var payment = Assert.Single(sink.Outputs.Values);
        Assert.Equal(10, payment.BlockHeight);
        Assert.Equal(2, payment.Confirmations);
        Assert.False(payment.IsMempool);
    }

    [Fact]
    public async Task Replaceable_transaction_input_is_detected_for_high_speed_policy()
    {
        var sink = new FakeSink();
        var reconciler = CreateReconciler(sink,
            [new BtcxAddressHistoryEntry(TxOne, 0)],
            [Transaction(TxOne, Output(0, "1", ReceiveScriptHex), signalsRbf: true)]);

        await reconciler.ReconcileInvoiceAsync("invoice-rbf", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(sink.Outputs.Values).SignalsRbf);
    }

    [Fact]
    public async Task Stale_noncanonical_and_indexer_missing_transactions_are_ignored()
    {
        var sink = new FakeSink();
        var stale = Transaction(TxOne, Output(0, "1", ReceiveScriptHex), BlockHash, 0, inActiveChain: false);
        var rpc = new FakeChainReader([stale]) { CanonicalBlockHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" };
        var reconciler = CreateReconciler(sink,
            [new BtcxAddressHistoryEntry(TxOne, 10), new BtcxAddressHistoryEntry(TxTwo, 0)], rpc);

        await reconciler.ReconcileInvoiceAsync("invoice-stale", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Outputs);
    }

    [Fact]
    public async Task Node_unavailable_is_not_mistaken_for_missing_transaction()
    {
        var rpc = new FakeChainReader([]) { Failure = new BtcxRpcUnavailableException("node offline", "getrawtransaction") };
        var reconciler = CreateReconciler(new FakeSink(), [new BtcxAddressHistoryEntry(TxOne, 0)], rpc);
        await Assert.ThrowsAsync<BtcxRpcUnavailableException>(() => reconciler.ReconcileInvoiceAsync(
            "invoice-rpc-down", BtcxNetworkId.Regtest, ReceiveScript, TestContext.Current.CancellationToken));
    }

    private static BtcxPaymentReconciler CreateReconciler(FakeSink sink, IReadOnlyList<BtcxAddressHistoryEntry> history, IReadOnlyList<BtcxDecodedTransaction> transactions) =>
        CreateReconciler(sink, history, new FakeChainReader(transactions));

    private static BtcxPaymentReconciler CreateReconciler(FakeSink sink, IReadOnlyList<BtcxAddressHistoryEntry> history, FakeChainReader rpc) =>
        new(new FakeHistoryClient(history), rpc, sink);

    private static BtcxDecodedTransaction Transaction(string txid, string outputs, string? blockHash = null, int? confirmations = null, bool? inActiveChain = null, bool signalsRbf = false)
    {
        using var json = JsonDocument.Parse("[" + outputs + "]");
        using var inputs = JsonDocument.Parse(signalsRbf ? "[{\"sequence\":4294967293}]" : "[]");
        return new BtcxDecodedTransaction
        {
            TxId = txid,
            Inputs = inputs.RootElement.Clone(),
            Outputs = json.RootElement.Clone(),
            BlockHash = blockHash,
            Confirmations = confirmations,
            InActiveChain = inActiveChain
        };
    }

    private static string Output(int index, string value, string scriptHex) =>
        $"{{\"value\":{value},\"n\":{index},\"scriptPubKey\":{{\"hex\":\"{scriptHex}\"}}}}";

    private sealed class FakeHistoryClient(IReadOnlyList<BtcxAddressHistoryEntry> history) : IBtcxAddressHistoryClient
    {
        public Task<IReadOnlyList<BtcxAddressHistoryEntry>> GetHistoryAsync(string scriptHash, CancellationToken cancellationToken = default)
        {
            Assert.Equal(64, scriptHash.Length);
            return Task.FromResult(history);
        }
    }

    private sealed class FakeChainReader(IReadOnlyList<BtcxDecodedTransaction> transactions) : IBtcxChainReader
    {
        public Exception? Failure { get; init; }
        public string CanonicalBlockHash { get; init; } = BlockHash;
        public Task<string> GetBlockHashAsync(int height, CancellationToken cancellationToken = default) => Task.FromResult(CanonicalBlockHash);

        public Task<BtcxDecodedTransaction> GetDecodedTransactionAsync(string txId, string? blockHash = null, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                return Task.FromException<BtcxDecodedTransaction>(Failure);
            var transaction = transactions.FirstOrDefault(value => value.TxId == txId);
            return transaction is null
                ? Task.FromException<BtcxDecodedTransaction>(new BtcxRpcException("missing", "getrawtransaction", -5))
                : Task.FromResult(transaction);
        }
    }

    private sealed class FakeSink : IBtcxObservedPaymentSink
    {
        public Dictionary<string, BtcxObservedOutput> Outputs { get; } = new(StringComparer.Ordinal);

        public Task<bool> RecordAsync(string invoiceId, BtcxObservedOutput output, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Outputs.TryAdd(invoiceId + ":" + output.PaymentId, output));
        }

        public Task SynchronizeInvoiceAsync(string invoiceId, IReadOnlyCollection<BtcxObservedOutput> currentOutputs, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
