using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BTCX.Payments;

public sealed class BtcxPaymentLinkExtension : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = PaymentTypes.CHAIN.GetPaymentMethodId(Plugin.CryptoCode);

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        => CreatePaymentLink(prompt);

    public static string? CreatePaymentLink(PaymentPrompt prompt)
    {
        if (prompt.Details is null || string.IsNullOrWhiteSpace(prompt.Destination))
            return null;
        try
        {
            var snapshot = JsonConvert.DeserializeObject<BtcxInvoiceSnapshot>(prompt.Details.ToString(Formatting.None));
            return snapshot is null ? null : BtcxPaymentUri.Create(prompt.Destination, snapshot);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }
}

/// <summary>Produces the canonical Phoenix PoCX payment URI from an immutable invoice snapshot.</summary>
public static class BtcxPaymentUri
{
    public static string? Create(string destination, BtcxInvoiceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(destination) ||
            !string.Equals(destination, snapshot.ReceiveAddress, StringComparison.Ordinal) ||
            snapshot.CryptoAmountAtomicUnits is not > 0 || snapshot.Network is null || snapshot.ScriptPubKeyHex is null)
            return null;

        var network = BtcxNetworkParameters.All.SingleOrDefault(parameters =>
            string.Equals(parameters.ChainName, snapshot.Network, StringComparison.Ordinal));
        if (network is null)
            return null;
        try
        {
            var address = BtcxAddress.Parse(destination, network.Id);
            if (!address.ScriptPubKey.ToBytes().AsSpan().SequenceEqual(Convert.FromHexString(snapshot.ScriptPubKeyHex)))
                return null;
            var amount = BtcxAmount.FromAtomicUnits(snapshot.CryptoAmountAtomicUnits.Value);
            if (BtcxAmount.FromDecimal(snapshot.CryptoAmount).AtomicUnits != amount.AtomicUnits)
                return null;

            var fixedAmount = amount.ToDecimal().ToString("0.########", CultureInfo.InvariantCulture);
            return $"btcx:{destination}?amount={fixedAmount}";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }
}
