using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Plugins.BTCX;
using BTCPayServer.Plugins.BTCX.Rpc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BTCX.Wallet;

/// <summary>
/// Allocates a unique witness-v0 address in a dedicated PoCX wallet. The node
/// wallet label is the durable idempotency/recovery key if invoice persistence
/// is interrupted after address generation.
/// </summary>
public sealed class BtcxReceiveAddressProvider(
    Func<IBtcxRpcClient> rpcClientFactory,
    IOptions<BtcxWalletOptions> options) : IBtcxReceiveAddressProvider
{
    private static readonly SemaphoreSlim AllocationLock = new(1, 1);

    public async Task<BtcxReceiveAddress> GetOrAllocateAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        var settings = options.Value.Validate();
        var label = CreateLabel(invoiceId);
        await AllocationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rpcClient = rpcClientFactory();
            await rpcClient.VerifyNetworkAsync(settings.Network, cancellationToken).ConfigureAwait(false);
            var labeled = await GetLabeledReceiveAddressesAsync(rpcClient, settings.WalletName, label, cancellationToken).ConfigureAwait(false);
            if (labeled.Length == 0)
            {
                // getnewaddress is state changing and is deliberately not retried
                // after transport ambiguity. A later call recovers from its label.
                await rpcClient.GetNewAddressAsync(settings.WalletName, label, "bech32", cancellationToken).ConfigureAwait(false);
                labeled = await GetLabeledReceiveAddressesAsync(rpcClient, settings.WalletName, label, cancellationToken).ConfigureAwait(false);
            }

            if (labeled.Length == 0)
                throw new BtcxRpcProtocolException("PoCX wallet did not persist the newly allocated receive address under its invoice label.");

            // Multiple entries can only result from a historical retry or a
            // concurrent BTCPay instance; choose deterministically for recovery.
            var address = labeled.OrderBy(value => value, StringComparer.Ordinal).First();
            var parsed = BtcxAddress.Parse(address, settings.Network);
            if (parsed.Type != BtcxAddressType.WitnessV0)
                throw new BtcxRpcProtocolException("PoCX wallet returned an unsupported receive address type.");
            var script = parsed.ScriptPubKey;
            var scriptHash = Convert.ToHexString(SHA256.HashData(script.ToBytes())).ToLowerInvariant();
            return new BtcxReceiveAddress(address, script, "btcx-script:" + scriptHash,
                BtcxNetworkParameters.For(settings.Network).ChainName);
        }
        finally
        {
            AllocationLock.Release();
        }
    }

    private static async Task<string[]> GetLabeledReceiveAddressesAsync(IBtcxRpcClient rpcClient, string walletName, string label, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await rpcClient.GetAddressesByLabelAsync(walletName, label, cancellationToken).ConfigureAwait(false);
            return addresses.Where(pair => string.Equals(pair.Value.Purpose, "receive", StringComparison.Ordinal))
                .Select(pair => pair.Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
        catch (BtcxRpcException ex) when (ex.RpcCode == -11)
        {
            // Pinned PoCX wallet source uses RPC_WALLET_INVALID_LABEL_NAME (-11)
            // when getaddressesbylabel has no matches.
            return [];
        }
    }

    private static string CreateLabel(string invoiceId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(invoiceId));
        return "btcx-invoice-" + Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }
}
