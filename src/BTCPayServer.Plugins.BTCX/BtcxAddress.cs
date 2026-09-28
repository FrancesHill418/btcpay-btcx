using System.Numerics;
using System.Security.Cryptography;
using NBitcoin;

namespace BTCPayServer.Plugins.BTCX;

public enum BtcxAddressType
{
    P2Pkh,
    P2Sh,
    WitnessV0,
    WitnessV1
}

/// <summary>BTCX address decoder/encoder using the PoCX prefixes and HRPs.</summary>
public static class BtcxAddress
{
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const string Bech32Alphabet = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private const uint Bech32Constant = 1;
    private const uint Bech32mConstant = 0x2bc830a3;

    public static (Script ScriptPubKey, BtcxAddressType Type) Parse(string address, BtcxNetworkId expectedNetwork)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var network = BtcxNetworkParameters.For(expectedNetwork);

        // A Bech32-looking value is decoded as such first. This ensures a valid
        // foreign BTCX HRP is reported as a network mismatch, never as Base58.
        if (LooksLikeBech32(address))
        {
            var decoded = DecodeWitnessAddress(address);
            var actualNetwork = BtcxNetworkParameters.All.FirstOrDefault(n =>
                string.Equals(decoded.Hrp, n.Bech32Hrp, StringComparison.OrdinalIgnoreCase));
            if (actualNetwork is null)
                throw new FormatException($"Unknown BTCX witness HRP '{decoded.Hrp}'.");
            if (actualNetwork.Id != expectedNetwork)
                throw new FormatException($"BTCX address is for {actualNetwork.Id}, not {expectedNetwork}.");
            return (new Script(WitnessScript(decoded.Version, decoded.Program)),
                decoded.Version == 0 ? BtcxAddressType.WitnessV0 : BtcxAddressType.WitnessV1);
        }

        var payload = DecodeBase58Check(address);
        if (payload.Length != 21)
            throw new FormatException("BTCX Base58 address payload must be 21 bytes.");
        var p2pkh = payload[0] == network.P2pkhPrefix;
        var p2sh = payload[0] == network.P2shPrefix;
        if (!p2pkh && !p2sh)
        {
            var otherNetwork = BtcxNetworkParameters.All.FirstOrDefault(n =>
                payload[0] == n.P2pkhPrefix || payload[0] == n.P2shPrefix);
            if (otherNetwork is not null)
                throw new FormatException($"BTCX address is for {otherNetwork.Id}, not {expectedNetwork}.");
            throw new FormatException($"Unsupported BTCX Base58 version 0x{payload[0]:x2}.");
        }

        var hash = payload.AsSpan(1, 20);
        var script = p2pkh
            ? new byte[] { 0x76, 0xa9, 0x14 }.Concat(hash.ToArray()).Concat(new byte[] { 0x88, 0xac }).ToArray()
            : new byte[] { 0xa9, 0x14 }.Concat(hash.ToArray()).Concat(new byte[] { 0x87 }).ToArray();
        return (new Script(script), p2pkh ? BtcxAddressType.P2Pkh : BtcxAddressType.P2Sh);
    }

    public static string FromScriptPubKey(Script scriptPubKey, BtcxNetworkId networkId)
    {
        ArgumentNullException.ThrowIfNull(scriptPubKey);
        var network = BtcxNetworkParameters.For(networkId);
        var script = scriptPubKey.ToBytes();

        if (script.Length == 25 && script.AsSpan(0, 3).SequenceEqual(new byte[] { 0x76, 0xa9, 0x14 }) &&
            script.AsSpan(23, 2).SequenceEqual(new byte[] { 0x88, 0xac }))
            return EncodeBase58Check([network.P2pkhPrefix, .. script.AsSpan(3, 20).ToArray()]);

        if (script.Length == 23 && script.AsSpan(0, 2).SequenceEqual(new byte[] { 0xa9, 0x14 }) && script[22] == 0x87)
            return EncodeBase58Check([network.P2shPrefix, .. script.AsSpan(2, 20).ToArray()]);

        if (script.Length >= 4 && script[1] == script.Length - 2 && script[1] is >= 2 and <= 40 &&
            (script[0] == 0x00 || script[0] is >= 0x51 and <= 0x51))
        {
            var version = script[0] == 0 ? 0 : 1;
            var program = script.AsSpan(2).ToArray();
            ValidateWitnessProgram(version, program);
            return EncodeWitnessAddress(network.Bech32Hrp, version, program);
        }

        throw new NotSupportedException("BTCX address conversion supports P2PKH, P2SH, witness v0 and witness v1 scriptPubKeys.");
    }

    private static byte[] WitnessScript(int version, byte[] program)
    {
        ValidateWitnessProgram(version, program);
        return [version == 0 ? (byte)0 : (byte)0x51, (byte)program.Length, .. program];
    }

    private static void ValidateWitnessProgram(int version, byte[] program)
    {
        if (version is < 0 or > 1)
            throw new FormatException("BTCX supports witness versions 0 and 1, as accepted by Phoenix PoCX.");
        if (program.Length is < 2 or > 40 || (version == 0 && program.Length is not (20 or 32)))
            throw new FormatException("Invalid BTCX witness program length.");
    }

    private static bool LooksLikeBech32(string value) => value.Contains('1') &&
        (value.StartsWith("pocx1", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("tpocx1", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("rpocx1", StringComparison.OrdinalIgnoreCase));

    private static string EncodeWitnessAddress(string hrp, int version, byte[] program)
    {
        ValidateWitnessProgram(version, program);
        var data = new List<byte> { (byte)version };
        data.AddRange(ConvertBits(program, 8, 5, pad: true));
        var checksumConstant = version == 0 ? Bech32Constant : Bech32mConstant;
        var values = HrpExpand(hrp).Concat(data).Concat(new byte[6]).ToArray();
        var polymod = Polymod(values) ^ checksumConstant;
        var checksum = Enumerable.Range(0, 6).Select(i => (byte)((polymod >> (5 * (5 - i))) & 31));
        return hrp + "1" + string.Concat(data.Concat(checksum).Select(v => Bech32Alphabet[v]));
    }

    private static (string Hrp, int Version, byte[] Program) DecodeWitnessAddress(string address)
    {
        if (address.Length is < 8 or > 90)
            throw new FormatException("BTCX witness address length is invalid.");
        var hasLower = address.Any(char.IsLower);
        var hasUpper = address.Any(char.IsUpper);
        if (hasLower && hasUpper)
            throw new FormatException("Mixed-case Bech32 address is invalid.");
        var normalized = address.ToLowerInvariant();
        var separator = normalized.LastIndexOf('1');
        if (separator < 1 || separator + 7 > normalized.Length)
            throw new FormatException("BTCX witness address separator/checksum is invalid.");
        var hrp = normalized[..separator];
        var data = normalized[(separator + 1)..].Select(c =>
        {
            var index = Bech32Alphabet.IndexOf(c);
            return index < 0 ? throw new FormatException("Invalid Bech32 character.") : (byte)index;
        }).ToArray();
        var check = Polymod(HrpExpand(hrp).Concat(data).ToArray());
        if (check is not Bech32Constant and not Bech32mConstant)
            throw new FormatException("BTCX witness address checksum is invalid.");
        var payload = data[..^6];
        if (payload.Length < 1)
            throw new FormatException("BTCX witness address is missing its version.");
        var version = payload[0];
        if (version > 1)
            throw new FormatException("BTCX supports witness versions 0 and 1, as accepted by Phoenix PoCX.");
        if ((version == 0 && check != Bech32Constant) || (version == 1 && check != Bech32mConstant))
            throw new FormatException("BTCX witness version has the wrong Bech32 checksum variant.");
        var program = ConvertBits(payload[1..], 5, 8, pad: false).ToArray();
        ValidateWitnessProgram(version, program);
        return (hrp, version, program);
    }

    private static IEnumerable<byte> ConvertBits(IEnumerable<byte> source, int fromBits, int toBits, bool pad)
    {
        var accumulator = 0;
        var bits = 0;
        var maxValue = (1 << toBits) - 1;
        foreach (var value in source)
        {
            if ((value >> fromBits) != 0)
                throw new FormatException("Invalid Bech32 data value.");
            accumulator = ((accumulator << fromBits) | value) & ((1 << (fromBits + toBits - 1)) - 1);
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                yield return (byte)((accumulator >> bits) & maxValue);
            }
        }
        if (pad)
        {
            if (bits > 0)
                yield return (byte)((accumulator << (toBits - bits)) & maxValue);
        }
        else if (bits >= fromBits || ((accumulator << (toBits - bits)) & maxValue) != 0)
        {
            throw new FormatException("Invalid Bech32 padding.");
        }
    }

    private static byte[] HrpExpand(string hrp) => hrp.Select(c => (byte)(c >> 5))
        .Concat(new byte[] { 0 }).Concat(hrp.Select(c => (byte)(c & 31))).ToArray();

    private static uint Polymod(IEnumerable<byte> values)
    {
        ReadOnlySpan<uint> generators = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        uint check = 1;
        foreach (var value in values)
        {
            var top = check >> 25;
            check = ((check & 0x1ffffff) << 5) ^ value;
            for (var i = 0; i < 5; i++)
                if (((top >> i) & 1) != 0)
                    check ^= generators[i];
        }
        return check;
    }

    private static byte[] DecodeBase58Check(string address)
    {
        if (address.Length is < 26 or > 35)
            throw new FormatException("BTCX Base58 address length is invalid.");
        BigInteger number = BigInteger.Zero;
        foreach (var character in address)
        {
            var digit = Base58Alphabet.IndexOf(character);
            if (digit < 0)
                throw new FormatException("Invalid Base58 character.");
            number = number * 58 + digit;
        }
        var significant = number.IsZero ? [] : number.ToByteArray(isUnsigned: true, isBigEndian: true);
        var leadingZeros = address.TakeWhile(c => c == '1').Count();
        var decoded = new byte[leadingZeros + significant.Length];
        significant.CopyTo(decoded, leadingZeros);
        if (decoded.Length < 5)
            throw new FormatException("BTCX Base58Check payload is too short.");
        var payload = decoded[..^4];
        var checksum = SHA256.HashData(SHA256.HashData(payload))[..4];
        if (!CryptographicOperations.FixedTimeEquals(checksum, decoded[^4..]))
            throw new FormatException("BTCX Base58Check checksum is invalid.");
        return payload;
    }

    private static string EncodeBase58Check(byte[] payload)
    {
        var checksum = SHA256.HashData(SHA256.HashData(payload))[..4];
        var bytes = payload.Concat(checksum).ToArray();
        var zeros = bytes.TakeWhile(b => b == 0).Count();
        var number = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var encoded = new List<char>();
        while (number > 0)
        {
            number = BigInteger.DivRem(number, 58, out var remainder);
            encoded.Add(Base58Alphabet[(int)remainder]);
        }
        encoded.AddRange(Enumerable.Repeat('1', zeros));
        encoded.Reverse();
        return new string(encoded.ToArray());
    }
}
