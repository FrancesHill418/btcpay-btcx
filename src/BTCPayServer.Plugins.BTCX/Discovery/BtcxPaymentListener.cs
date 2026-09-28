using System.Collections.Concurrent;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Payments;
using BTCPayServer.Plugins.BTCX.Rpc;
using BTCPayServer.Plugins.BTCX.Wallet;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BTCX.Discovery;

/// <summary>
/// Polls electrs-btcx for script history. Electrs is the only discovery source;
/// its results are reconciled against the PoCX node before payment persistence.
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
    private readonly ConcurrentDictionary<string, InvoiceEntity> _trackedInvoices = new(StringComparer.Ordinal);
    private bool _addressCatalogLoaded;
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
                    // Exception bodies from endpoints, config and remote peers are not logged.
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
        if (wallet.Network == BtcxNetworkId.Mainnet)
            throw new InvalidOperationException("Development BTCX payment listener refuses mainnet.");

        var rpcClient = rpcClientFactory();
        await rpcClient.VerifyNetworkAsync(wallet.Network, cancellationToken).ConfigureAwait(false);
        await LoadTrackedInvoicesAsync(cancellationToken).ConfigureAwait(false);

        var activeInvoices = await invoiceRepository.GetMonitoredInvoices(_paymentMethodId, includeNonActivated: true, cancellationToken)
            .ConfigureAwait(false);
        foreach (var invoice in activeInvoices)
            _trackedInvoices[invoice.Id] = invoice;

        var reconciler = new BtcxPaymentReconciler(historyClient, rpcClient, paymentSink);
        foreach (var invoice in _trackedInvoices.Values.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetReceiveScript(invoice, wallet.Network, out var script))
                continue;
            try
            {
                await reconciler.ReconcileInvoiceAsync(invoice.Id, wallet.Network, script, cancellationToken).ConfigureAwait(false);
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

    private async Task LoadTrackedInvoicesAsync(CancellationToken cancellationToken)
    {
        if (_addressCatalogLoaded)
            return;

        string[] ids;
        using (var context = invoiceRepository.DbContextFactory.CreateContext())
        {
            ids = await context.AddressInvoices.AsNoTracking()
                .Where(address => address.PaymentMethodId == _paymentMethodId.ToString())
                .Select(address => address.InvoiceDataId)
                .Distinct()
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var offset = 0; offset < ids.Length; offset += InvoiceBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(offset).Take(InvoiceBatchSize).ToArray();
#pragma warning disable CS0618 // BTCPay v2.4.4 exposes this persisted invoice retrieval overload.
            var invoices = await invoiceRepository.GetInvoices(batch).ConfigureAwait(false);
#pragma warning restore CS0618
            foreach (var invoice in invoices)
                _trackedInvoices[invoice.Id] = invoice;
        }
        _addressCatalogLoaded = true;
    }

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
}
