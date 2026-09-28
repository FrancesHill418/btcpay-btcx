namespace BTCPayServer.Plugins.BTCX.Rpc;

public interface IBtcxRpcClient
{
    Task<BtcxBlockchainInfo> GetBlockchainInfoAsync(CancellationToken cancellationToken = default);
    Task<BtcxNetworkInfo> GetNetworkInfoAsync(CancellationToken cancellationToken = default);
    Task<string> GetBestBlockHashAsync(CancellationToken cancellationToken = default);
    Task<int> GetBlockCountAsync(CancellationToken cancellationToken = default);
    Task<BtcxBlockInfo> GetBlockAsync(string blockHash, int verbosity = 2, CancellationToken cancellationToken = default);
    Task<string> GetRawBlockAsync(string blockHash, CancellationToken cancellationToken = default);
    Task<BtcxDecodedTransaction> GetDecodedTransactionAsync(string txId, string? blockHash = null, CancellationToken cancellationToken = default);
    Task<string> GetRawTransactionAsync(string txId, string? blockHash = null, CancellationToken cancellationToken = default);
    Task<BtcxWalletTransaction> GetTransactionAsync(string txId, string walletName, bool includeDecoded = true, CancellationToken cancellationToken = default);
    Task<BtcxTransactionConfirmationInfo> GetTransactionConfirmationsAsync(string txId, string? blockHash = null, CancellationToken cancellationToken = default);
    Task<BtcxBlockchainInfo> VerifyNetworkAsync(BtcxNetworkId expectedNetwork, CancellationToken cancellationToken = default);
}
