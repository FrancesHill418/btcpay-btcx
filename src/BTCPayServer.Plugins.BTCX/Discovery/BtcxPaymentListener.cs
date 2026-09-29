using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Payments;
using BTCPayServer.Plugins.BTCX.Rpc;
using BTCPayServer.Plugins.BTCX.Wallet;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BTCX.Discovery;

/// <summary>
/// Polls electrs-btcx for script history. It scans BTCPay's active invoices
/// plus invoices with a recent BTCX settlement event for reorg recovery.
/// </summary>
public sealed class BtcxPaymentListener(
    InvoiceRepository invoiceRepository,
    BtcxPaymentMethodHandler paymentMethodHandler,
    IBtcxAddressHistoryClient historyClient,
    Func<IBtcxRpcClient> rpcClientFactory,
    IBtcxObservedPaymentSink paymentSink,
    IOptionsMonitor<BtcxElectrumOptions> electrumOptions,
    IOptionsMonitor<BtcxWalletOptions> walletOptions,
    ILogger<BtcxPaymentListener> logger) : BackgroundService
{
    private const int InvoiceBatchSize = 250;
    private const string RecentSettlementQuery = """
        SELECT ai."InvoiceDataId" AS "InvoiceId",
               GREATEST(MAX(p."Created"), COALESCE(MAX(e."Timestamp") FILTER (
                   WHERE e."Message" LIKE '% new event: invoice_paymentSettled (1014)'), '-infinity'::timestamptz)) AS "LastSeen"
        FROM "AddressInvoices" ai
        JOIN "Payments" p ON p."InvoiceDataId" = ai."InvoiceDataId"
                         AND p."PaymentMethodId" = ai."PaymentMethodId"
        LEFT JOIN "InvoiceEvents" e ON e."InvoiceDataId" = ai."InvoiceDataId"
        WHERE ai."PaymentMethodId" = @PaymentMethodId
          AND p."Status" IN ('Settled', 'Unaccounted')
        GROUP BY ai."InvoiceDataId"
        HAVING MAX(p."Created") >= @Cutoff OR
               MAX(e."Timestamp") FILTER (
                   WHERE e."Message" LIKE '% new event: invoice_paymentSettled (1014)') >= @Cutoff
        """;

    private readonly Dictionary<string, RecentInvoice> _recentSettledInvoices = new(StringComparer.Ordinal);
    private bool _recentSettlementsLoaded;
    private readonly PaymentMethodId _paymentMethodId = paymentMethodHandler.PaymentMethodId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = electrumOptions.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    await RunCycleAsync(options, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("BTCX payment scan failed ({FailureType}); it will retry after the configured interval.", ex.GetType().Name);
                }
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollIntervalSeconds, 2, 3600)), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(BtcxElectrumOptions options, CancellationToken cancellationToken)
    {
        _ = options.Validate();
        var wallet = walletOptions.CurrentValue.Validate();

        var rpcClient = rpcClientFactory();
        await rpcClient.VerifyNetworkAsync(wallet.Network, cancellationToken).ConfigureAwait(false);
        await LoadRecentSettledInvoicesAsync(options.ReorgSafetyWindowHours, cancellationToken).ConfigureAwait(false);

        var activeInvoices = await invoiceRepository.GetMonitoredInvoices(_paymentMethodId, includeNonActivated: true, cancellationToken)
            .ConfigureAwait(false);
        var invoicesToReconcile = activeInvoices.ToDictionary(invoice => invoice.Id, StringComparer.Ordinal);
        foreach (var recent in _recentSettledInvoices.Values)
            invoicesToReconcile.TryAdd(recent.Invoice.Id, recent.Invoice);

        foreach (var id in _recentSettledInvoices.Where(pair => !BtcxInvoiceTrackingPolicy.ShouldPoll(
                     isActive: false, pair.Value.LastSeen, DateTimeOffset.UtcNow, TimeSpan.FromHours(options.ReorgSafetyWindowHours)))
                 .Select(pair => pair.Key).ToArray())
            _recentSettledInvoices.Remove(id);

        var reconciler = new BtcxPaymentReconciler(historyClient, rpcClient, paymentSink);
        foreach (var invoice in invoicesToReconcile.Values.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetReceiveScript(invoice, wallet.Network, out var script))
                continue;
            try
            {
                var wasSettled = IsSettledBtcxInvoice(invoice);
                await reconciler.ReconcileInvoiceAsync(invoice.Id, wallet.Network, script, cancellationToken).ConfigureAwait(false);
                var current = await invoiceRepository.GetInvoice(invoice.Id).ConfigureAwait(false);
                if (IsSettledBtcxInvoice(current))
                {
                    var lastSeen = wasSettled && _recentSettledInvoices.TryGetValue(invoice.Id, out var existing)
                        ? existing.LastSeen
                        : DateTimeOffset.UtcNow;
                    _recentSettledInvoices[invoice.Id] = new RecentInvoice(current, lastSeen);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is BtcxElectrumUnavailableException or BtcxElectrumProtocolException or
                                           BtcxRpcUnavailableException or BtcxRpcAuthenticationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("BTCX history reconciliation failed for invoice {InvoiceId} ({FailureType}).", invoice.Id, ex.GetType().Name);
            }
        }
    }

    private async Task LoadRecentSettledInvoicesAsync(int windowHours, CancellationToken cancellationToken)
    {
        if (_recentSettlementsLoaded)
            return;

        string[] ids;
        Dictionary<string, DateTimeOffset> lastSeen;
        using (var context = invoiceRepository.DbContextFactory.CreateContext())
        {
            var rows = await context.Database.GetDbConnection().QueryAsync<(string InvoiceId, DateTimeOffset LastSeen)>(
                new CommandDefinition(RecentSettlementQuery, new
                {
                    PaymentMethodId = _paymentMethodId.ToString(),
                    Cutoff = DateTimeOffset.UtcNow.AddHours(-windowHours)
                }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            lastSeen = rows.ToDictionary(row => row.InvoiceId, row => row.LastSeen, StringComparer.Ordinal);
            ids = lastSeen.Keys.ToArray();
        }

        for (var offset = 0; offset < ids.Length; offset += InvoiceBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(offset).Take(InvoiceBatchSize).ToArray();
#pragma warning disable CS0618 // BTCPay v2.4.4 exposes this persisted invoice retrieval overload.
            var invoices = await invoiceRepository.GetInvoices(batch).ConfigureAwait(false);
#pragma warning restore CS0618
            foreach (var invoice in invoices)
                _recentSettledInvoices[invoice.Id] = new RecentInvoice(invoice, lastSeen[invoice.Id]);
        }
        _recentSettlementsLoaded = true;
    }

    private bool IsSettledBtcxInvoice(InvoiceEntity invoice) => invoice is not null &&
        invoice.GetPayments(false).Any(payment => payment.PaymentMethodId == _paymentMethodId &&
                                                  payment.Status == PaymentStatus.Settled);

    private bool TryGetReceiveScript(InvoiceEntity invoice, BtcxNetworkId expectedNetwork, out Script script)
    {
        script = new Script(Array.Empty<byte>());
        var prompt = invoice.GetPaymentPrompt(_paymentMethodId);
        if (prompt?.Details is null || string.IsNullOrWhiteSpace(prompt.Destination))
            return false;
        try
        {
            if (paymentMethodHandler.ParsePaymentPromptDetails(prompt.Details) is not BtcxInvoiceSnapshot snapshot ||
                snapshot.Network != BtcxNetworkParameters.For(expectedNetwork).ChainName ||
                string.IsNullOrWhiteSpace(snapshot.ReceiveAddress) ||
                !string.Equals(snapshot.ReceiveAddress, prompt.Destination, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(snapshot.ScriptPubKeyHex))
                return false;
            var addressScript = BtcxAddress.Parse(snapshot.ReceiveAddress, expectedNetwork).ScriptPubKey;
            var snapshotScript = new Script(Convert.FromHexString(snapshot.ScriptPubKeyHex));
            if (!addressScript.ToBytes().AsSpan().SequenceEqual(snapshotScript.ToBytes()))
                return false;
            script = snapshotScript;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonSerializationException or NotSupportedException or ArgumentException)
        {
            logger.LogError("BTCX invoice {InvoiceId} has an invalid persisted receiving destination.", invoice.Id);
            return false;
        }
    }

    private sealed record RecentInvoice(InvoiceEntity Invoice, DateTimeOffset LastSeen);
}

public static class BtcxInvoiceTrackingPolicy
{
    public static bool ShouldPoll(bool isActive, DateTimeOffset? lastSeen, DateTimeOffset now, TimeSpan reorgSafetyWindow) =>
        isActive || (lastSeen is not null && lastSeen.Value >= now - reorgSafetyWindow);
}
