using System.Globalization;
using System.Text.Json;

namespace VehicleLocator.Application.Vehicles;

public enum ParseKind { Change, Tombstone, Invalid }

public sealed record VehicleParseResult(
    ParseKind Kind,
    VehicleChange? Change = null,
    string? Error = null);

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
                        var change = new VehicleChange(
                            after.GetProperty("id").GetString()!,
                            VehicleChangeKind.Upsert,
                            after.GetProperty("state").GetString()!,
                            ToMicros(after.GetProperty("updated_at").GetString()!),
                            lsn);
                        return new VehicleParseResult(ParseKind.Change, change);
                    }
                case "d":
                    {
                        // Com REPLICA IDENTITY FULL o "before" vem completo.
                        var before = root.GetProperty("before");
                        var change = new VehicleChange(
                            before.GetProperty("id").GetString()!,
                            VehicleChangeKind.Delete,
                            null,
                            ToMicros(before.GetProperty("updated_at").GetString()!),
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

    private static VehicleParseResult Invalid(string error) =>
        new(ParseKind.Invalid, null, error);

    private static long ToMicros(string iso)
    {
        var dto = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        return (dto.UtcTicks - DateTime.UnixEpoch.Ticks) / 10;
    }
}
