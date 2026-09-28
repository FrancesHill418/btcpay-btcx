using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Rates;
using BTCPayServer.Plugins.BTCX.Wallet;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BTCX.Payments;

public sealed class BtcxPaymentMethodHandler(
    ManualRateSettingsService settingsService,
    IBtcxReceiveAddressProvider receiveAddressProvider) : IPaymentMethodHandler
{
    private readonly ManualRateSettingsService _settingsService = settingsService;
    private readonly IBtcxReceiveAddressProvider _receiveAddressProvider = receiveAddressProvider;

    public PaymentMethodId PaymentMethodId { get; } = PaymentTypes.CHAIN.GetPaymentMethodId(Plugin.CryptoCode);
    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

    public async Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = Plugin.CryptoCode;
        context.Prompt.Divisibility = Plugin.Divisibility;
        context.State = await _settingsService.GetValidEnabledAsync();
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        if (context.State is not ManualBtcxRateSettings fetchedSettings ||
            !ManualRateSettingsService.IsValid(fetchedSettings))
        {
            throw new PaymentMethodUnavailableException("BTCX is disabled or its manual BTCX/CNY rate is not configured.");
        }

        var currentSettings = await _settingsService.GetValidEnabledAsync();
        if (currentSettings is null ||
            currentSettings.BtcxCnyRate != fetchedSettings.BtcxCnyRate ||
            currentSettings.UpdatedAt != fetchedSettings.UpdatedAt)
        {
            throw new PaymentMethodUnavailableException("The BTCX/CNY rate changed while this invoice was being created. Please retry.");
        }

        if (!context.InvoiceEntity.TryGetRate(Plugin.CryptoCode, out var appliedRate) ||
            appliedRate != fetchedSettings.BtcxCnyRate)
        {
            throw new PaymentMethodUnavailableException("The invoice did not use the configured manual BTCX/CNY rate.");
        }

        var promptAmount = context.Prompt.Calculate().Due;
        BtcxAmount atomicAmount;
        try
        {
            atomicAmount = BtcxAmount.FromDecimal(promptAmount);
        }
        catch (ArgumentException ex)
        {
            throw new PaymentMethodUnavailableException($"BTCX invoice amount cannot be represented in atomic units: {ex.Message}");
        }
        var receive = await _receiveAddressProvider.GetOrAllocateAsync(context.InvoiceEntity.Id);
        context.Prompt.Destination = receive.Address;
        context.TrackedDestinations.Add(receive.TrackingToken);
        context.Prompt.Details = JObject.FromObject(new BtcxInvoiceSnapshot(
            FiatAmount: context.InvoiceEntity.Price,
            FiatCurrency: context.InvoiceEntity.Currency,
            CryptoAmount: promptAmount,
            CryptoCurrency: Plugin.CryptoCode,
            ExchangeRate: appliedRate,
            RateSource: ManualRateSettingsService.Source,
            RateTimestamp: fetchedSettings.UpdatedAt!.Value,
            CryptoAmountAtomicUnits: atomicAmount.AtomicUnits,
            Network: receive.Network,
            ReceiveAddress: receive.Address,
            ScriptPubKeyHex: Convert.ToHexString(receive.ScriptPubKey.ToBytes()).ToLowerInvariant()), Serializer);
    }

    public object ParsePaymentMethodConfig(JToken config) => config.ToObject<BtcxPaymentMethodConfig>(Serializer) ?? new BtcxPaymentMethodConfig();

    public object ParsePaymentPromptDetails(JToken details) => details.ToObject<BtcxInvoiceSnapshot>(Serializer)
        ?? throw new FormatException("Invalid BTCX invoice quote snapshot.");

    public object ParsePaymentDetails(JToken details) => details.ToObject<BtcxPaymentDetails>(Serializer)
        ?? throw new FormatException("Invalid BTCX payment details.");

    public Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext)
    {
        validationContext.Config = JObject.FromObject(new BtcxPaymentMethodConfig(), Serializer);
        validationContext.StripUnknownProperties = true;
        return Task.CompletedTask;
    }
}

public sealed class BtcxPaymentMethodConfig { }
