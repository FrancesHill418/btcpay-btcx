using BTCPayServer.Plugins.BTCX.Payments;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxPaymentUriTests
{
    private const string Address = "rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt";
    private static readonly string ScriptHex = Convert.ToHexString(BtcxAddress.Parse(Address, BtcxNetworkId.Regtest).ScriptPubKey.ToBytes()).ToLowerInvariant();

    [Theory]
    [InlineData("37.5", "btcx:rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt?amount=37.5")]
    [InlineData("0.1", "btcx:rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt?amount=0.1")]
    [InlineData("0.00000001", "btcx:rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt?amount=0.00000001")]
    [InlineData("1", "btcx:rpocx1qcpueamxr0aa82t7dtvhzdksq59c993f93lzedt?amount=1")]
    public void Phoenix_canonical_uri_uses_fixed_decimal_address_and_amount(string amountText, string expectedUri)
    {
        var amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = Snapshot(amount);

        var uri = BtcxPaymentUri.Create(Address, snapshot);

        Assert.Equal(expectedUri, uri);
        // Phoenix v2.4.0 parsePaymentUri strips the btcx: scheme, then URLSearchParams
        // reads amount and Number(amount) returns the requested positive BTCX value.
        var parsed = Assert.IsType<string>(uri);
        Assert.StartsWith("btcx:", parsed, StringComparison.Ordinal);
        var fields = parsed[5..].Split("?amount=", StringSplitOptions.None);
        Assert.Equal(Address, fields[0]);
        Assert.Equal(amount, decimal.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void URI_refuses_wrong_network_or_mismatching_snapshot_script()
    {
        Assert.Null(BtcxPaymentUri.Create(Address, Snapshot(1m) with { Network = "test" }));
        Assert.Null(BtcxPaymentUri.Create(Address, Snapshot(1m) with { ScriptPubKeyHex = "0014" + new string('0', 40) }));
        Assert.Null(BtcxPaymentUri.Create("not-an-address", Snapshot(1m)));
    }

    [Fact]
    public void Payment_link_extension_uses_the_persisted_invoice_snapshot()
    {
        var prompt = new PaymentPrompt
        {
            Destination = Address,
            Details = JObject.FromObject(Snapshot(0.00000001m))
        };

        Assert.Equal("btcx:" + Address + "?amount=0.00000001", BtcxPaymentLinkExtension.CreatePaymentLink(prompt));
    }

    private static BtcxInvoiceSnapshot Snapshot(decimal amount) => new(
        3750m, "CNY", amount, "BTCX", 100m, "manual", DateTimeOffset.UnixEpoch,
        BtcxAmount.FromDecimal(amount).AtomicUnits, "regtest", Address, ScriptHex);
}
