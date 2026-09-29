using BTCPayServer.Plugins.BTCX.Discovery;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxInvoiceTrackingPolicyTests
{
    [Fact]
    public void Active_invoice_remains_monitored_regardless_of_age()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(BtcxInvoiceTrackingPolicy.ShouldPoll(
            isActive: true, lastSeen: now.AddYears(-10), now, TimeSpan.FromHours(72)));
    }

    [Fact]
    public void Recently_settled_invoice_is_monitored_inside_reorg_window_after_restart()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(BtcxInvoiceTrackingPolicy.ShouldPoll(
            isActive: false, lastSeen: now.AddHours(-71), now, TimeSpan.FromHours(72)));
        Assert.True(BtcxInvoiceTrackingPolicy.ShouldPoll(
            isActive: false, lastSeen: now.AddHours(-72), now, TimeSpan.FromHours(72)));
    }

    [Fact]
    public void Historical_settled_invoice_is_not_monitored_after_reorg_window()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(BtcxInvoiceTrackingPolicy.ShouldPoll(
            isActive: false, lastSeen: now.AddHours(-73), now, TimeSpan.FromHours(72)));
        Assert.False(BtcxInvoiceTrackingPolicy.ShouldPoll(
            isActive: false, lastSeen: null, now, TimeSpan.FromHours(72)));
    }
}
