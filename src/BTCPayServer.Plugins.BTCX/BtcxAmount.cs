using System.Globalization;

namespace BTCPayServer.Plugins.BTCX;

/// <summary>Exact BTCX amount represented in 10^-8 BTCX atomic units.</summary>
public readonly record struct BtcxAmount : IComparable<BtcxAmount>
{
    public const int Decimals = 8;
    public const long AtomicUnitsPerBtcx = 100_000_000;
    public const long MaxMoneyAtomicUnits = 21_000_000L * AtomicUnitsPerBtcx;
    public const long MinimumPositiveAtomicUnits = 1;

    public long AtomicUnits { get; }

    private BtcxAmount(long atomicUnits) => AtomicUnits = atomicUnits;

    public static BtcxAmount Zero => new(0);
    public static BtcxAmount MinimumPositive => new(MinimumPositiveAtomicUnits);
    public static BtcxAmount Maximum => new(MaxMoneyAtomicUnits);

    public static BtcxAmount FromAtomicUnits(long atomicUnits)
    {
        if (atomicUnits < 0 || atomicUnits > MaxMoneyAtomicUnits)
            throw new ArgumentOutOfRangeException(nameof(atomicUnits), "BTCX amount is outside the consensus money range.");
        return new BtcxAmount(atomicUnits);
    }

    public static BtcxAmount FromDecimal(decimal amount)
    {
        if (amount < 0m || amount > 21_000_000m)
            throw new ArgumentOutOfRangeException(nameof(amount), "BTCX amount is outside the consensus money range.");

        var scaled = amount * AtomicUnitsPerBtcx;
        if (scaled != decimal.Truncate(scaled))
            throw new ArgumentException("BTCX amounts support at most 8 decimal places.", nameof(amount));
        return FromAtomicUnits(decimal.ToInt64(scaled));
    }

    /// <summary>Parses invariant fixed-point text; exponent notation and excess precision are rejected.</summary>
    public static BtcxAmount Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Any(c => c is < '0' or > '9' && c != '.'))
            throw new FormatException("BTCX amount must contain unsigned invariant decimal digits only.");
        var point = text.IndexOf('.');
        if (point != text.LastIndexOf('.') || point == 0)
            throw new FormatException("BTCX amount has an invalid decimal point.");
        var fractionalDigits = point < 0 ? 0 : text.Length - point - 1;
        if (fractionalDigits > Decimals)
            throw new FormatException("BTCX amount supports at most 8 decimal places.");
        if (point >= 0 && fractionalDigits == 0)
            throw new FormatException("BTCX amount must have digits after the decimal point.");
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("BTCX amount is not a representable decimal value.");
        return FromDecimal(value);
    }

    public decimal ToDecimal() => AtomicUnits / (decimal)AtomicUnitsPerBtcx;

    public string ToFixedString(bool trimTrailingZeros = true)
    {
        var value = ToDecimal().ToString("F8", CultureInfo.InvariantCulture);
        return trimTrailingZeros ? value.TrimEnd('0').TrimEnd('.') : value;
    }

    public int CompareTo(BtcxAmount other) => AtomicUnits.CompareTo(other.AtomicUnits);
    public override string ToString() => ToFixedString();
}
