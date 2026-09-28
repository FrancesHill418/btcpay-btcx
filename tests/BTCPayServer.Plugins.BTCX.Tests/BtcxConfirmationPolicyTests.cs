using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BTCX.Payments;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxConfirmationPolicyTests
{
    [Theory]
    [InlineData(SpeedPolicy.HighSpeed, false, 0)]
    [InlineData(SpeedPolicy.HighSpeed, true, 1)]
    [InlineData(SpeedPolicy.MediumSpeed, false, 1)]
    [InlineData(SpeedPolicy.LowMediumSpeed, false, 2)]
    [InlineData(SpeedPolicy.LowSpeed, false, 6)]
    public void Required_confirmation_count_matches_btcpay_v244_policy(SpeedPolicy speedPolicy, bool signalsRbf, int expected)
    {
        Assert.Equal(expected, BtcxPaymentServiceSink.RequiredConfirmations(speedPolicy, signalsRbf));
    }
}
