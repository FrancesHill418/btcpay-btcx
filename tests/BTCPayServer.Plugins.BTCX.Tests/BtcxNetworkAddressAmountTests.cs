using BTCPayServer.Plugins.BTCX;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public class BtcxNetworkAddressAmountTests
{
    [Fact]
    public void Network_parameters_match_pinned_bitcoin_pocx_source()
    {
        Assert.Equal("a73c915e", BtcxNetworkParameters.Mainnet.MessageMagicHex);
        Assert.Equal(8338, BtcxNetworkParameters.Mainnet.DefaultP2pPort);
        Assert.Equal(8332, BtcxNetworkParameters.Mainnet.RpcPort);
        Assert.Equal((byte)0x55, BtcxNetworkParameters.Mainnet.P2pkhPrefix);
        Assert.Equal((byte)0x5a, BtcxNetworkParameters.Mainnet.P2shPrefix);
        Assert.Equal("pocx", BtcxNetworkParameters.Mainnet.Bech32Hrp);
        Assert.Equal("6ab422073e327d42a0e5dfaaa26564324ddb225e53c64da89283cd4e3dfb7ac6", BtcxNetworkParameters.Mainnet.GenesisHash);

        Assert.Equal("6df248b4", BtcxNetworkParameters.Testnet.MessageMagicHex);
        Assert.Equal(18338, BtcxNetworkParameters.Testnet.DefaultP2pPort);
        Assert.Equal(18332, BtcxNetworkParameters.Testnet.RpcPort);
        Assert.Equal((byte)0x7f, BtcxNetworkParameters.Testnet.P2pkhPrefix);
        Assert.Equal((byte)0x84, BtcxNetworkParameters.Testnet.P2shPrefix);
        Assert.Equal("tpocx", BtcxNetworkParameters.Testnet.Bech32Hrp);
        Assert.Equal("181c51a172fe20c203e463f6f203b7d9be388fa0f1282e507192f94d24a57e81", BtcxNetworkParameters.Testnet.GenesisHash);

        Assert.Equal("fabfb5da", BtcxNetworkParameters.Regtest.MessageMagicHex);
        Assert.Equal(18444, BtcxNetworkParameters.Regtest.DefaultP2pPort);
        Assert.Equal(18443, BtcxNetworkParameters.Regtest.RpcPort);
        Assert.Equal((byte)0x6f, BtcxNetworkParameters.Regtest.P2pkhPrefix);
        Assert.Equal((byte)0xc4, BtcxNetworkParameters.Regtest.P2shPrefix);
        Assert.Equal("rpocx", BtcxNetworkParameters.Regtest.Bech32Hrp);
        Assert.Equal("2a98a52253aeff06093948b00568d380b7634621bc606403127973c9acbbfde0", BtcxNetworkParameters.Regtest.GenesisHash);
        Assert.All(BtcxNetworkParameters.All, network => Assert.Equal(120, network.TargetBlockSeconds));
        Assert.All(BtcxNetworkParameters.All, network => Assert.Equal(286, network.BlockHeaderLength));
        Assert.All(BtcxNetworkParameters.All, network => Assert.Equal(3000, network.DefaultDustRelayFeeSatPerKvB));
    }

    [Theory]
    [InlineData("pocx1qcpueamxr0aa82t7dtvhzdksq59c993f9heu9te", "0014c0799eecc37f7a752fcd5b2e26da00a17052c525", BtcxNetworkId.Mainnet)]
    [InlineData("tpocx1qcpueamxr0aa82t7dtvhzdksq59c993f90zw6pr", "0014c0799eecc37f7a752fcd5b2e26da00a17052c525", BtcxNetworkId.Testnet)]
    [InlineData("rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt", "0014c0799eecc37f7a752fcd5b2e26da00a17052c525", BtcxNetworkId.Regtest)]
    [InlineData("pocx1qc7axsl082uqm0t3fuqefy7rw2ug52pl338esjk", "0014c7ba687de75701b7ae29e03292786e57114507f1", BtcxNetworkId.Mainnet)]
    public void Phoenix_wallet_witness_fixture_roundtrips_to_script(string address, string expectedScript, BtcxNetworkId network)
    {
        var parsed = BtcxAddress.Parse(address, network);
        Assert.Equal(expectedScript, Convert.ToHexString(parsed.ScriptPubKey.ToBytes()).ToLowerInvariant());
        Assert.Equal(address, BtcxAddress.FromScriptPubKey(parsed.ScriptPubKey, network));
        Assert.Equal(BtcxAddressType.WitnessV0, parsed.Type);
    }

    [Theory]
    [InlineData("bPeT3fa3EDu8QgJcrjUzHRC8kZ28dJL9fp", "76a91477bff20c60e522dfaa3350c39b030a5d004e839a88ac", BtcxNetworkId.Mainnet)]
    [InlineData("dVsQaErJatVWiDcn1Nxi5T7b1jh1oNhCxe", "a914b472a266d0bd89c13706a4132ccfb16f7c3b9fcb87", BtcxNetworkId.Mainnet)]
    [InlineData("tAEFwL2HWZmjHdMPiSTzbqzGse2gKQWVok", "76a914243f1394f44554f4ce3fd68649c19adc483ce92488ac", BtcxNetworkId.Testnet)]
    [InlineData("vEnLRVhjqxdm6RvLte5ht2hBcJEWNiaX9A", "a9144e9f39ca4688ff102128ea4ccda34105324305b087", BtcxNetworkId.Testnet)]
    [InlineData("2MzQwSSnBHWHqSAqtTVQ6v47XtaisrJa1Vc", "a9144e9f39ca4688ff102128ea4ccda34105324305b087", BtcxNetworkId.Regtest)]
    public void Btcx_base58_fixtures_roundtrip_to_script(string address, string expectedScript, BtcxNetworkId network)
    {
        var parsed = BtcxAddress.Parse(address, network);
        Assert.Equal(expectedScript, Convert.ToHexString(parsed.ScriptPubKey.ToBytes()).ToLowerInvariant());
        Assert.Equal(address, BtcxAddress.FromScriptPubKey(parsed.ScriptPubKey, network));
    }

    [Fact]
    public void Rejects_bad_checksum_wrong_network_malformed_and_unsupported_type()
    {
        Assert.Throws<FormatException>(() => BtcxAddress.Parse("pocx1qcpueamxr0aa82t7dtvhzdksq59c993f9heuzte", BtcxNetworkId.Mainnet));
        Assert.Throws<FormatException>(() => BtcxAddress.Parse("pocx1qcpueamxr0aa82t7dtvhzdksq59c993f9heu9te", BtcxNetworkId.Testnet));
        Assert.Throws<FormatException>(() => BtcxAddress.Parse("not-an-address", BtcxNetworkId.Mainnet));
        Assert.Throws<FormatException>(() => BtcxAddress.Parse("pocx1zcpueamxr0aa82t7dtvhzdksq59c993f95stl5d", BtcxNetworkId.Mainnet));
        Assert.Throws<NotSupportedException>(() => BtcxAddress.FromScriptPubKey(new Script(new byte[] { 0x6a, 0x00 }), BtcxNetworkId.Mainnet));
    }

    [Fact]
    public void Taproot_witness_v1_uses_bech32m_and_roundtrips()
    {
        var scriptPubKey = new Script(new byte[] { 0x51, 0x20 }.Concat(Enumerable.Repeat((byte)0x11, 32)).ToArray());
        var address = BtcxAddress.FromScriptPubKey(scriptPubKey, BtcxNetworkId.Mainnet);
        Assert.StartsWith("pocx1p", address);
        var parsed = BtcxAddress.Parse(address, BtcxNetworkId.Mainnet);
        Assert.Equal(BtcxAddressType.WitnessV1, parsed.Type);
        Assert.Equal(scriptPubKey, parsed.ScriptPubKey);
    }

    [Fact]
    public void Base58_testnet_and_regtest_versions_are_network_specific()
    {
        const string testnetP2Sh = "vEnLRVhjqxdm6RvLte5ht2hBcJEWNiaX9A";
        const string regtestP2Sh = "2MzQwSSnBHWHqSAqtTVQ6v47XtaisrJa1Vc";
        Assert.Throws<FormatException>(() => BtcxAddress.Parse(testnetP2Sh, BtcxNetworkId.Regtest));
        Assert.Throws<FormatException>(() => BtcxAddress.Parse(regtestP2Sh, BtcxNetworkId.Testnet));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("0.00000001", 1L)]
    [InlineData("1", 100_000_000L)]
    [InlineData("37.5", 3_750_000_000L)]
    [InlineData("21000000", BtcxAmount.MaxMoneyAtomicUnits)]
    public void Parses_and_formats_exact_atomic_units(string text, long atomicUnits)
    {
        var amount = BtcxAmount.Parse(text);
        Assert.Equal(atomicUnits, amount.AtomicUnits);
        Assert.Equal(text, amount.ToFixedString());
    }

    [Fact]
    public void Amount_precision_range_and_underflow_are_checked()
    {
        Assert.Equal(1, BtcxAmount.MinimumPositive.AtomicUnits);
        Assert.Equal("0.00000001", BtcxAmount.MinimumPositive.ToFixedString());
        Assert.Equal(21_000_000m, BtcxAmount.Maximum.ToDecimal());
        Assert.Throws<FormatException>(() => BtcxAmount.Parse("0.000000001"));
        Assert.Throws<FormatException>(() => BtcxAmount.Parse("1e-8"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BtcxAmount.FromAtomicUnits(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BtcxAmount.FromAtomicUnits(BtcxAmount.MaxMoneyAtomicUnits + 1));
        Assert.Throws<ArgumentException>(() => BtcxAmount.FromDecimal(0.000000001m));
        Assert.Throws<ArgumentOutOfRangeException>(() => BtcxAmount.FromDecimal(21_000_000.00000001m));
    }
}
