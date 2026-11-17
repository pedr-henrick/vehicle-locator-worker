using System.Text.Json;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Application.Vehicles;

public sealed record VehicleParseResult(ParseKind Kind, VehicleChange? Change = null, string? Error = null);

public static class VehicleDebeziumParser
{
    public static VehicleParseResult Parse(string? value)
    {
        if (value is null)
            return new VehicleParseResult(ParseKind.Tombstone);

        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            var op = root.GetProperty("op").GetString();
            var lsn = root.GetProperty("source").GetProperty("lsn").GetInt64();

            switch (op)
            {
                case "c":
                case "r":
                case "u":
                    {
                        var after = root.GetProperty("after");
                        var state = RequiredString(after, "state");
                        if (state is not ("REGISTERED" or "ACTIVE" or "IDLE" or "MAINTENANCE" or "DECOMMISSIONED"))
                            return Invalid($"unsupported state '{state}'");

                        var change = new VehicleChange(
                            RequiredString(after, "id"),
                            VehicleChangeKind.Upsert,
                            state,
                            DebeziumTime.ToMicros(RequiredString(after, "updated_at")),
                            lsn);
                        return new VehicleParseResult(ParseKind.Change, change);
                    }
                case "d":
                    {
                        var before = root.GetProperty("before");
                        var change = new VehicleChange(
                            RequiredString(before, "id"),
                            VehicleChangeKind.Delete,
                            null,
                            DebeziumTime.ToMicros(RequiredString(before, "updated_at")),
                            lsn);
                        return new VehicleParseResult(ParseKind.Change, change);
                    }
                default:
                    return Invalid($"unsupported op '{op}'");
            }
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

    private static VehicleParseResult Invalid(string error) =>
        new(ParseKind.Invalid, null, error);
}
