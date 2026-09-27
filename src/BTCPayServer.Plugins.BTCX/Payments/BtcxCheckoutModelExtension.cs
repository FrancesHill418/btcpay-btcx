using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
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
        if (context.Prompt.Details is JObject details)
            context.Model.AdditionalData["btcxQuote"] = details.DeepClone();

        // This explicit marker is available to plugin-owned checkout UI. No address/payment
        // action is exposed by this skeleton.
        context.Model.AdditionalData["btcxPaymentMode"] = "skeleton-not-payable";
    }
}
