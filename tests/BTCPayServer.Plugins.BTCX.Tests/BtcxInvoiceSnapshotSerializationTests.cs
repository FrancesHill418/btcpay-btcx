using System.Globalization;
using BTCPayServer.Plugins.BTCX.Payments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace BTCPayServer.Plugins.BTCX.Tests;

public sealed class BtcxInvoiceSnapshotSerializationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 28, 10, 15, 30, TimeSpan.Zero);

    [Theory]
    [InlineData("iso")]
    [InlineData("unix-seconds")]
    [InlineData("unix-milliseconds")]
    [InlineData("unix-microseconds")]
    [InlineData("unix-ticks")]
    [InlineData("datetime-ticks")]
    public void Snapshot_timestamp_round_trips_or_reads_legacy_integer_formats(string representation)
    {
        var timestamp = representation switch
        {
            "iso" => JsonConvert.ToString(Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            "unix-seconds" => Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "unix-milliseconds" => Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            "unix-microseconds" => (Timestamp.ToUnixTimeMilliseconds() * 1000).ToString(CultureInfo.InvariantCulture),
            "unix-ticks" => ((Timestamp - DateTimeOffset.UnixEpoch).Ticks).ToString(CultureInfo.InvariantCulture),
            "datetime-ticks" => Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(representation))
        };
        var snapshotJson = "{\"fiatAmount\":7.5,\"fiatCurrency\":\"CNY\",\"cryptoAmount\":37.5,\"cryptoCurrency\":\"BTCX\",\"exchangeRate\":0.2,\"rateSource\":\"manual\",\"rateTimestamp\":" + timestamp + "}";
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        });
        var snapshot = JObject.Parse(snapshotJson).ToObject<BtcxInvoiceSnapshot>(serializer)!;

        Assert.Equal(Timestamp, snapshot.RateTimestamp);
        Assert.Equal(37.5m, snapshot.CryptoAmount);
        var written = JObject.FromObject(snapshot, serializer);
        Assert.Equal(JTokenType.String, written["rateTimestamp"]!.Type);
        Assert.Equal(Timestamp, DateTimeOffset.Parse(written["rateTimestamp"]!.Value<string>()!, CultureInfo.InvariantCulture));
    }
}
