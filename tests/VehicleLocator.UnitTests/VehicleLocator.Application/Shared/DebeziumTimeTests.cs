using VehicleLocator.Application.Shared;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Shared;

public class DebeziumTimeTests
{
    [Theory]
    [InlineData("1970-01-01T00:00:00Z", 0L)]
    [InlineData("1970-01-01T00:00:00.000001Z", 1L)]
    [InlineData("2026-01-01T00:00:00Z", 1767225600000000L)]
    [InlineData("2026-11-15T23:16:04.134788Z", 1794784564134788L)]
    public void Converts_iso_utc_to_unix_microseconds(string iso, long expected)
    {
        Assert.Equal(expected, DebeziumTime.ToMicros(iso));
    }

    [Fact]
    public void Offsets_are_normalized_to_utc()
    {
        Assert.Equal(
            DebeziumTime.ToMicros("2026-01-01T03:00:00Z"),
            DebeziumTime.ToMicros("2026-01-01T00:00:00-03:00"));
    }

    [Fact]
    public void Invalid_input_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => DebeziumTime.ToMicros("ontem"));
    }
}
