namespace BTCPayServer.Plugins.BTCX.Payments;

/// <summary>Persistent identity and last observed chain location for one transaction output.</summary>
public sealed record BtcxPaymentDetails(
    string TransactionId,
    uint OutputIndex,
    string Network,
    string ScriptPubKeyHex,
    string? BlockHash,
    int? BlockHeight,
    int Confirmations,
    bool IsMempool);
