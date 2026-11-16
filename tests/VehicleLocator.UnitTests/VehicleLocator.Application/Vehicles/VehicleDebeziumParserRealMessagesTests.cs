using VehicleLocator.Application.Shared;
using VehicleLocator.Application.Vehicles;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Vehicles;

public class VehicleDebeziumParserRealMessagesTests
{
    private const string RealInsert = """
        {"before":null,"after":{"id":"v-1","state":"REGISTERED","updated_at":"2026-11-15T23:16:04.134788Z"},"source":{"version":"3.0.8.Final","connector":"postgresql","name":"fleet","ts_ms":1794784564135,"snapshot":"false","db":"fleet","sequence":"[null,\"26812288\"]","ts_us":1794784564135739,"ts_ns":1794784564135739000,"schema":"public","table":"vehicles","txId":756,"lsn":26812288,"xmin":null},"transaction":null,"op":"c","ts_ms":1794784564486,"ts_us":1794784564486376,"ts_ns":1794784564486376761}
        """;

    private const string RealUpdate = """
        {"before":{"id":"v-1","state":"REGISTERED","updated_at":"2026-11-15T23:16:04.134788Z"},"after":{"id":"v-1","state":"ACTIVE","updated_at":"2026-11-15T23:20:49.643391Z"},"source":{"version":"3.0.8.Final","connector":"postgresql","name":"fleet","ts_ms":1794784849644,"snapshot":"false","db":"fleet","sequence":"[\"26813504\",\"26813944\"]","ts_us":1794784849644309,"ts_ns":1794784849644309000,"schema":"public","table":"vehicles","txId":761,"lsn":26813944,"xmin":null},"transaction":null,"op":"u","ts_ms":1794784849680,"ts_us":1794784849680620,"ts_ns":1794784849680620530}
        """;

    private const string RealDelete = """
        {"before":{"id":"v-1","state":"ACTIVE","updated_at":"2026-11-15T23:20:49.643391Z"},"after":null,"source":{"version":"3.0.8.Final","connector":"postgresql","name":"fleet","ts_ms":1794784857571,"snapshot":"false","db":"fleet","sequence":"[\"26814352\",\"26814408\"]","ts_us":1794784857571582,"ts_ns":1794784857571582000,"schema":"public","table":"vehicles","txId":762,"lsn":26814408,"xmin":null},"transaction":null,"op":"d","ts_ms":1794784857795,"ts_us":1794784857795961,"ts_ns":1794784857795961934}
        """;

    [Fact]
    public void Real_insert_is_parsed_with_exact_values()
    {
        var change = VehicleDebeziumParser.Parse(RealInsert).Change!;

        Assert.Equal("v-1", change.VehicleId);
        Assert.Equal(VehicleChangeKind.Upsert, change.Kind);
        Assert.Equal("REGISTERED", change.State);
        Assert.Equal(1794784564134788L, change.UpdatedAtMicros);
        Assert.Equal(26812288L, change.Lsn);
    }

    [Fact]
    public void Real_update_uses_after_values()
    {
        var change = VehicleDebeziumParser.Parse(RealUpdate).Change!;

        Assert.Equal("ACTIVE", change.State);
        Assert.Equal(1794784849643391L, change.UpdatedAtMicros);
        Assert.Equal(26813944L, change.Lsn);
    }

    [Fact]
    public void Real_delete_uses_before_and_event_lsn()
    {
        var change = VehicleDebeziumParser.Parse(RealDelete).Change!;

        Assert.Equal(VehicleChangeKind.Delete, change.Kind);
        Assert.Equal("v-1", change.VehicleId);
        Assert.Null(change.State);
        Assert.Equal(26814408L, change.Lsn);
    }

    [Theory]
    [InlineData("""{"before":null,"after":null,"source":{"lsn":1},"op":"u"}""")]
    [InlineData("""{"before":null,"after":{"id":"v-1","updated_at":"2026-01-01T00:00:00Z"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":"v-1","state":"X","updated_at":"ontem"},"source":{"lsn":1},"op":"c"}""")]
    [InlineData("""{"before":null,"after":{"id":"v-1","state":"X","updated_at":"2026-01-01T00:00:00Z"},"op":"c"}""")]
    public void Malformed_events_are_invalid_instead_of_throwing(string value)
    {
        var result = VehicleDebeziumParser.Parse(value);

        Assert.Equal(ParseKind.Invalid, result.Kind);
        Assert.NotNull(result.Error);
    }
}
