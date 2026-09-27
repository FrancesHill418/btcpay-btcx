using BTCPayServer;

namespace BTCPayServer.Plugins.BTCX.Rates;

public static class BtcxAmountCalculator
{
    public static decimal MinimumUnit(int divisibility)
    {
        if (divisibility is < 0 or > 28)
            throw new ArgumentOutOfRangeException(nameof(divisibility));
        return 1m / Pow10(divisibility);
    }

    public static decimal Calculate(decimal fiatAmount, decimal rate, int divisibility)
    {
        if (fiatAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(fiatAmount), "Invoice fiat amount must be positive.");
        if (rate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(rate), "BTCX/CNY rate must be positive.");
        if (divisibility is < 0 or > 28)
            throw new ArgumentOutOfRangeException(nameof(divisibility));

        try
        {
            var amount = fiatAmount / rate;
            // Match the v2.4.4 payment prompt's round-up behavior to avoid under-collecting.
            return Extensions.RoundUp(amount, divisibility);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(fiatAmount), $"Calculated BTCX amount exceeds decimal range: {ex.Message}");
        }
    }

    private static decimal Pow10(int exponent)
    {
        var value = 1m;
        for (var i = 0; i < exponent; i++)
            value *= 10m;
        return value;
    }
}
