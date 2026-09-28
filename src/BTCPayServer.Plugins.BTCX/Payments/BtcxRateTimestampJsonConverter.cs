using System.Globalization;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BTCX.Payments;

/// <summary>
/// Writes one canonical UTC ISO-8601 timestamp while accepting timestamps already
/// persisted as strings or Unix/DateTime ticks by earlier BTCPay serializer paths.
/// </summary>
public sealed class BtcxRateTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset ReadJson(JsonReader reader, Type objectType, DateTimeOffset existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Date)
        {
            var value = reader.Value switch
            {
                DateTimeOffset dateTimeOffset => dateTimeOffset,
                DateTime dateTime => new DateTimeOffset(dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime),
                _ => throw new JsonSerializationException("BTCX rate timestamp has an invalid date value.")
            };
            return value.ToUniversalTime();
        }
        if (reader.TokenType == JsonToken.String &&
            DateTimeOffset.TryParse((string?)reader.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed.ToUniversalTime();
        if (reader.TokenType != JsonToken.Integer)
            throw new JsonSerializationException("BTCX rate timestamp must be an ISO-8601 string or integer timestamp.");

        long numeric;
        try
        {
            numeric = Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new JsonSerializationException("BTCX rate timestamp integer is outside the supported range.");
        }

        try
        {
            var magnitude = Math.Abs((decimal)numeric);
            if (magnitude < 100_000_000_000m)
                return DateTimeOffset.FromUnixTimeSeconds(numeric);
            if (magnitude < 100_000_000_000_000m)
                return DateTimeOffset.FromUnixTimeMilliseconds(numeric);
            if (magnitude < 10_000_000_000_000_000m)
                return DateTimeOffset.UnixEpoch.AddTicks(numeric * 10);
            if (magnitude < 100_000_000_000_000_000m)
                return DateTimeOffset.UnixEpoch.AddTicks(numeric);
            return new DateTimeOffset(numeric, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonSerializationException("BTCX rate timestamp integer is outside the supported date range.", ex);
        }
    }

    public override void WriteJson(JsonWriter writer, DateTimeOffset value, JsonSerializer serializer) =>
        writer.WriteValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}
