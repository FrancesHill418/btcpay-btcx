using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BTCX.Payments;

public sealed class BtcxCheckoutModelExtension : ICheckoutModelExtension
{
    public PaymentMethodId PaymentMethodId { get; } = PaymentTypes.CHAIN.GetPaymentMethodId(Plugin.CryptoCode);
    public string Image => "btcx.svg";
    public string Badge => "BTCX";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context.Prompt.Details is not JObject)
            return;
        var link = BtcxPaymentLinkExtension.CreatePaymentLink(context.Prompt);
        if (link is null)
            return;

        // Reuse BTCPay v2.4.4's existing on-chain checkout component: it renders the
        // address, copy action, wallet link and QR from these standard model fields.
        context.Model.CheckoutBodyComponentName = BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
        context.Model.InvoiceBitcoinUrl = link;
        context.Model.InvoiceBitcoinUrlQR = link;
    }
}
