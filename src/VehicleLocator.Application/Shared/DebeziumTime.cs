using System.Globalization;

namespace VehicleLocator.Application.Shared;

public static class DebeziumTime
{
    public static long ToMicros(string iso)
    {
        var dto = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return (dto.UtcTicks - DateTime.UnixEpoch.Ticks) / 10;
    }
}
