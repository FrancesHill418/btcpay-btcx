namespace BTCPayServer.Plugins.BTCX;

public enum BtcxNetworkId
{
    Mainnet,
    Testnet,
    Regtest
}

/// <summary>Bitcoin-PoCX network identity and address parameters.</summary>
public sealed record BtcxNetworkParameters(
    BtcxNetworkId Id,
    string ChainName,
    string MessageMagicHex,
    int DefaultP2pPort,
    int RpcPort,
    byte P2pkhPrefix,
    byte P2shPrefix,
    string Bech32Hrp,
    string GenesisHash,
    int TargetBlockSeconds)
{
    public static BtcxNetworkParameters Mainnet { get; } = new(
        BtcxNetworkId.Mainnet, "main", "a73c915e", 8338, 8332,
        0x55, 0x5a, "pocx",
        "6ab422073e327d42a0e5dfaaa26564324ddb225e53c64da89283cd4e3dfb7ac6", 120);

    public static BtcxNetworkParameters Testnet { get; } = new(
        BtcxNetworkId.Testnet, "test", "6df248b4", 18338, 18332,
        0x7f, 0x84, "tpocx",
        "181c51a172fe20c203e463f6f203b7d9be388fa0f1282e507192f94d24a57e81", 120);

    public static BtcxNetworkParameters Regtest { get; } = new(
        BtcxNetworkId.Regtest, "regtest", "fabfb5da", 18444, 18443,
        0x6f, 0xc4, "rpocx",
        "2a98a52253aeff06093948b00568d380b7634621bc606403127973c9acbbfde0", 120);

    public static IReadOnlyList<BtcxNetworkParameters> All { get; } = [Mainnet, Testnet, Regtest];

    /// <summary>
    /// Bitcoin-PoCX uses 286-byte headers (versus Bitcoin's 80 bytes) and
    /// zeros the trailing 65-byte signature before hashing.
    /// </summary>
    public int BlockHeaderLength => 286;

    /// <summary>Bitcoin-PoCX Core's compiled dust relay fee default, in sat/kvB. Node policy can override it.</summary>
    public int DefaultDustRelayFeeSatPerKvB => 3000;

    public static BtcxNetworkParameters For(BtcxNetworkId id) => id switch
    {
        BtcxNetworkId.Mainnet => Mainnet,
        BtcxNetworkId.Testnet => Testnet,
        BtcxNetworkId.Regtest => Regtest,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
