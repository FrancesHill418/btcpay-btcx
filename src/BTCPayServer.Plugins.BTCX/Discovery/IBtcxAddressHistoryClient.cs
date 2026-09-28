namespace BTCPayServer.Plugins.BTCX.Discovery;

public sealed record BtcxAddressHistoryEntry(string TxId, int Height);

public interface IBtcxAddressHistoryClient
{
    Task<IReadOnlyList<BtcxAddressHistoryEntry>> GetHistoryAsync(string scriptHash, CancellationToken cancellationToken = default);
}
