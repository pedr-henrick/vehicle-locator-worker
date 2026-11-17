using System.Text.Json;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Application.Positions;

public sealed record PositionParseResult(ParseKind Kind, PositionChange? Change = null, string? Error = null);

public static class PositionDebeziumParser
{
    public static PositionParseResult Parse(string? value)
    {
        if (value is null)
            return new PositionParseResult(ParseKind.Tombstone);

        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            var op = root.GetProperty("op").GetString();
            var lsn = root.GetProperty("source").GetProperty("lsn").GetInt64();

            if (op is not ("c" or "r" or "u"))
                return Invalid($"unsupported op '{op}'");

            var after = root.GetProperty("after");
            var vehicleId = RequiredString(after, "vehicle_id");
            var lat = after.GetProperty("lat").GetDouble();
            var lng = after.GetProperty("lng").GetDouble();

            if (!double.IsFinite(lat) || lat is < -90 or > 90)
                return Invalid($"lat out of range: {lat}");
            if (!double.IsFinite(lng) || lng is < -180 or > 180)
                return Invalid($"lng out of range: {lng}");

            var change = new PositionChange(
                vehicleId,
                lat,
                lng,
                DebeziumTime.ToMicros(RequiredString(after, "recorded_at")),
                lsn);

            return new PositionParseResult(ParseKind.Change, change);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                       or InvalidOperationException or FormatException)
        {
            return Invalid(ex.Message);
        }
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"'{propertyName}' is missing or empty")
            : value;
    }

    private static PositionParseResult Invalid(string error) => new(ParseKind.Invalid, null, error);
}
