using NBitcoin;

namespace BTCPayServer.Plugins.BTCX.Wallet;

public sealed record BtcxReceiveAddress(string Address, Script ScriptPubKey, string TrackingToken, string Network);

public interface IBtcxReceiveAddressProvider
{
    Task<BtcxReceiveAddress> GetOrAllocateAsync(string invoiceId, CancellationToken cancellationToken = default);
}
