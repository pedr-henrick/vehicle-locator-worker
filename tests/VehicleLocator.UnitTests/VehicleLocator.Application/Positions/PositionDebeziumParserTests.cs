using VehicleLocator.Application.Positions;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Positions;

public class PositionDebeziumParserTests
{
    private const string RealInsert = """
        {"before":null,"after":{"id":1,"vehicle_id":"v-1","lat":-9.66,"lng":-35.73,"recorded_at":"2026-11-15T23:16:09.214383Z"},"source":{"version":"3.0.8.Final","connector":"postgresql","name":"fleet","ts_ms":1794784569219,"snapshot":"false","db":"fleet","sequence":"[\"26812600\",\"26812760\"]","ts_us":1794784569219383,"ts_ns":1794784569219383000,"schema":"public","table":"positions","txId":757,"lsn":26812760,"xmin":null},"transaction":null,"op":"c","ts_ms":1794784569573,"ts_us":1794784569573679,"ts_ns":1794784569573679132}
        """;

    private const string BatchA = """
        {"before":null,"after":{"id":8,"vehicle_id":"v-4","lat":-9.6,"lng":-35.7,"recorded_at":"2026-11-16T00:13:49.511824Z"},"source":{"snapshot":"false","table":"positions","txId":757,"lsn":26802696},"op":"c"}
        """;

    private const string BatchB = """
        {"before":null,"after":{"id":20,"vehicle_id":"v-4","lat":-9.6,"lng":-35.7,"recorded_at":"2026-11-16T00:13:49.511824Z"},"source":{"snapshot":"false","table":"positions","txId":757,"lsn":26804640},"op":"c"}
        """;

    [Fact]
    public void Real_insert_is_parsed_with_exact_values()
    {
        var r = PositionDebeziumParser.Parse(RealInsert);

        Assert.Equal(ParseKind.Change, r.Kind);
        Assert.Equal("v-1", r.Change!.VehicleId);
        Assert.Equal(-9.66, r.Change.Lat);
        Assert.Equal(-35.73, r.Change.Lng);
        Assert.Equal(1794784569214383L, r.Change.RecordedAtMicros);
        Assert.Equal(26812760L, r.Change.Lsn);
    }

    [Fact]
    public void Snapshot_read_is_parsed_like_an_insert()
    {
        var snapshot = RealInsert.Replace("\"op\":\"c\"", "\"op\":\"r\"");

        Assert.Equal(ParseKind.Change, PositionDebeziumParser.Parse(snapshot).Kind);
    }

    [Fact]
    public void Same_recorded_at_with_different_lsn_keeps_both_values_for_tiebreak()
    {
        var a = PositionDebeziumParser.Parse(BatchA).Change!;
        var b = PositionDebeziumParser.Parse(BatchB).Change!;

        Assert.Equal(a.RecordedAtMicros, b.RecordedAtMicros);
        Assert.True(b.Lsn > a.Lsn);
    }

    [Fact]
    public void Null_value_is_tombstone()
    {
        Assert.Equal(ParseKind.Tombstone, PositionDebeziumParser.Parse(null).Kind);
    }

    [Fact]
    public void Microsecond_precision_is_preserved()
    {
        var a = PositionDebeziumParser.Parse(RealInsert).Change!.RecordedAtMicros;
        var b = PositionDebeziumParser.Parse(
            RealInsert.Replace("23:16:09.214383Z", "23:16:09.214384Z")).Change!.RecordedAtMicros;

        Assert.Equal(1, b - a);
    }

    [Fact]
    public void Delete_op_is_rejected_because_positions_are_never_removed()
    {
        var delete = RealInsert.Replace("\"op\":\"c\"", "\"op\":\"d\"");

        Assert.Equal(ParseKind.Invalid, PositionDebeziumParser.Parse(delete).Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"before":null,"after":null,"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"lat":1,"lng":1,"recorded_at":"2026-01-01T00:00:00Z"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"vehicle_id":"","lat":1,"lng":1,"recorded_at":"2026-01-01T00:00:00Z"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"vehicle_id":"v","lat":91,"lng":1,"recorded_at":"2026-01-01T00:00:00Z"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"vehicle_id":"v","lat":1,"lng":181,"recorded_at":"2026-01-01T00:00:00Z"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"vehicle_id":"v","lat":"x","lng":1,"recorded_at":"2026-01-01T00:00:00Z"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"vehicle_id":"v","lat":1,"lng":1,"recorded_at":"ontem"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":1,"vehicle_id":"v","lat":1,"lng":1,"recorded_at":"2026-01-01T00:00:00Z"},"op":"c"}""")]
    public void Malformed_events_are_invalid_instead_of_throwing(string value)
    {
        var r = PositionDebeziumParser.Parse(value);

        Assert.Equal(ParseKind.Invalid, r.Kind);
        Assert.NotNull(r.Error);
    }
}
