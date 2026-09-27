using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BTCX.Payments;

public sealed class BtcxPaymentLinkExtension : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = PaymentTypes.CHAIN.GetPaymentMethodId(Plugin.CryptoCode);

    // No BTCX address or URI scheme is generated until a wallet/node integration exists.
    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper) => null;
}
