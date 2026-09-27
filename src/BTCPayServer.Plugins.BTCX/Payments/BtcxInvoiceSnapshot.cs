namespace BTCPayServer.Plugins.BTCX.Payments;

public sealed record BtcxInvoiceSnapshot(
    decimal FiatAmount,
    string FiatCurrency,
    decimal CryptoAmount,
    string CryptoCurrency,
    decimal ExchangeRate,
    string RateSource,
    DateTimeOffset RateTimestamp);
