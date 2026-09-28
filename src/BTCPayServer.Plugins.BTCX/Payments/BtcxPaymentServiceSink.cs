using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Discovery;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BTCX.Payments;

/// <summary>Persists each verified transaction output using BTCPay's payment services.</summary>
public sealed class BtcxPaymentServiceSink(
    InvoiceRepository invoiceRepository,
    PaymentService paymentService,
    BtcxPaymentMethodHandler paymentMethodHandler,
    EventAggregator eventAggregator,
    ILogger<BtcxPaymentServiceSink> logger) : IBtcxObservedPaymentSink, IDisposable
{
    private readonly MemoryCache _seenOutpoints = new(new MemoryCacheOptions { SizeLimit = 50_000 });
    private readonly PaymentMethodId _paymentMethodId = paymentMethodHandler.PaymentMethodId;

    public async Task<bool> RecordAsync(string invoiceId, BtcxObservedOutput output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = invoiceId + ":" + output.PaymentId;
        if (_seenOutpoints.TryGetValue(key, out _))
            return false;
        var invoice = await invoiceRepository.GetInvoice(invoiceId).ConfigureAwait(false);
        var prompt = invoice?.GetPaymentPrompt(_paymentMethodId);
        if (invoice is null || prompt?.Details is null || prompt.Destination is null)
            return false;

        var snapshot = paymentMethodHandler.ParsePaymentPromptDetails(prompt.Details) as BtcxInvoiceSnapshot;
        if (snapshot is null || snapshot.Network != output.Network || snapshot.ReceiveAddress != prompt.Destination ||
            snapshot.ScriptPubKeyHex is null || !string.Equals(snapshot.ScriptPubKeyHex, output.ScriptPubKeyHex, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Ignoring BTCX output because invoice {InvoiceId} snapshot does not match its receiving script.", invoiceId);
            return false;
        }
        if (invoice.GetPayments(false).Any(payment => payment.PaymentMethodId == _paymentMethodId && payment.Id == output.PaymentId))
        {
            Remember(key);
            return false;
        }

        var amount = BtcxAmount.FromAtomicUnits(output.AmountAtomicUnits);
        var paymentData = new PaymentData
        {
            Id = output.PaymentId,
            Created = DateTimeOffset.UtcNow,
            Status = PaymentStatus.Processing,
            Amount = amount.ToDecimal(),
            Currency = Plugin.CryptoCode
        }.Set(invoice, paymentMethodHandler, new BtcxPaymentDetails(
            output.TransactionId,
            output.OutputIndex,
            output.Network,
            output.ScriptPubKeyHex,
            output.BlockHash,
            output.BlockHeight,
            output.Confirmations,
            output.IsMempool,
            output.SignalsRbf));

        var payment = await paymentService.AddPayment(paymentData, [output.TransactionId]).ConfigureAwait(false);
        if (payment is null)
        {
            var current = await invoiceRepository.GetInvoice(invoiceId).ConfigureAwait(false);
            if (current?.GetPayments(false).Any(existing => existing.PaymentMethodId == _paymentMethodId && existing.Id == output.PaymentId) is true)
            {
                Remember(key);
                return false;
            }
            throw new InvalidOperationException("BTCPay did not persist the observed BTCX output.");
        }
        Remember(key);
        eventAggregator.Publish(new InvoiceEvent(payment.InvoiceEntity, InvoiceEvent.ReceivedPayment) { Payment = payment });
        eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoiceId));
        return true;
    }

    public async Task SynchronizeInvoiceAsync(string invoiceId, IReadOnlyCollection<BtcxObservedOutput> currentOutputs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invoice = await invoiceRepository.GetInvoice(invoiceId).ConfigureAwait(false);
        if (invoice is null)
            return;
        var current = currentOutputs.ToDictionary(output => output.PaymentId, StringComparer.Ordinal);
        var updates = new List<PaymentEntity>();
        foreach (var payment in invoice.GetPayments(false).Where(payment => payment.PaymentMethodId == _paymentMethodId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = paymentMethodHandler.ParsePaymentDetails(payment.Details) as BtcxPaymentDetails;
            if (details is null)
                continue;
            if (!current.TryGetValue(payment.Id, out var output))
            {
                if (payment.Status != PaymentStatus.Unaccounted)
                {
                    payment.Status = PaymentStatus.Unaccounted;
                    updates.Add(payment);
                }
                continue;
            }

            var snapshot = new BtcxPaymentDetails(output.TransactionId, output.OutputIndex, output.Network,
                output.ScriptPubKeyHex, output.BlockHash, output.BlockHeight, output.Confirmations,
                output.IsMempool, output.SignalsRbf);
            var status = IsSettled(invoice.SpeedPolicy, output.Confirmations, output.SignalsRbf)
                ? PaymentStatus.Settled
                : PaymentStatus.Processing;
            if (details != snapshot || payment.Status != status)
            {
                payment.SetDetails(paymentMethodHandler, snapshot);
                payment.Status = status;
                updates.Add(payment);
            }
        }

        if (updates.Count == 0)
            return;
        await paymentService.UpdatePayments(updates).ConfigureAwait(false);
        eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoiceId));
    }

    public static int RequiredConfirmations(SpeedPolicy speedPolicy, bool signalsRbf) => speedPolicy switch
    {
        SpeedPolicy.HighSpeed when signalsRbf => 1,
        SpeedPolicy.HighSpeed => 0,
        SpeedPolicy.MediumSpeed => 1,
        SpeedPolicy.LowMediumSpeed => 2,
        SpeedPolicy.LowSpeed => 6,
        _ => 6
    };

    private static bool IsSettled(SpeedPolicy speedPolicy, int confirmations, bool signalsRbf) =>
        RequiredConfirmations(speedPolicy, signalsRbf) <= confirmations;

    private void Remember(string key) => _seenOutpoints.Set(key, true, new MemoryCacheEntryOptions { Size = 1 });

    public void Dispose() => _seenOutpoints.Dispose();
}
