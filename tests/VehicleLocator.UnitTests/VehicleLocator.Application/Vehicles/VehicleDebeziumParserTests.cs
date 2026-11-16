using VehicleLocator.Application.Vehicles;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Vehicles
{
    public class VehicleDebeziumParserTests
    {
        private const string Insert = """
        {"before":null,"after":{"id":"v-1","state":"REGISTERED","updated_at":"2026-11-15T23:16:04.134788Z"},
         "source":{"snapshot":"false","lsn":26812288},"op":"c"}
        """;

        private const string Update = """
        {"before":{"id":"v-1","state":"REGISTERED","updated_at":"2026-11-15T23:16:04.134788Z"},
         "after":{"id":"v-1","state":"ACTIVE","updated_at":"2026-11-15T23:20:49.643391Z"},
         "source":{"snapshot":"false","lsn":26813944},"op":"u"}
        """;

        private const string Delete = """
        {"before":{"id":"v-1","state":"ACTIVE","updated_at":"2026-11-15T23:20:49.643391Z"},"after":null,
         "source":{"snapshot":"false","lsn":26814408},"op":"d"}
        """;

        [Fact]
        public void Insert_is_parsed_as_upsert()
        {
            var r = VehicleDebeziumParser.Parse(Insert);

            Assert.Equal(ParseKind.Change, r.Kind);
            Assert.Equal("v-1", r.Change!.VehicleId);
            Assert.Equal(VehicleChangeKind.Upsert, r.Change.Kind);
            Assert.Equal("REGISTERED", r.Change.State);
            Assert.Equal(26812288, r.Change.Lsn);
        }

        [Fact]
        public void Update_uses_after()
        {
            var r = VehicleDebeziumParser.Parse(Update);

            Assert.Equal("ACTIVE", r.Change!.State);
            Assert.Equal(26813944, r.Change.Lsn);
        }

        [Fact]
        public void Delete_uses_before_and_has_no_state()
        {
            var r = VehicleDebeziumParser.Parse(Delete);

            Assert.Equal(VehicleChangeKind.Delete, r.Change!.Kind);
            Assert.Equal("v-1", r.Change.VehicleId);
            Assert.Null(r.Change.State);
        }

        [Fact]
        public void Snapshot_read_is_parsed_as_upsert()
        {
            var snapshot = Insert.Replace("\"op\":\"c\"", "\"op\":\"r\"");

            Assert.Equal(VehicleChangeKind.Upsert, VehicleDebeziumParser.Parse(snapshot).Change!.Kind);
        }

        [Fact]
        public void Null_value_is_tombstone()
        {
            Assert.Equal(ParseKind.Tombstone, VehicleDebeziumParser.Parse(null).Kind);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("""{"op":"x","source":{"lsn":1}}""")]
        public void Garbage_is_invalid(string value)
        {
            Assert.Equal(ParseKind.Invalid, VehicleDebeziumParser.Parse(value).Kind);
        }

        [Fact]
        public void Microsecond_precision_is_preserved()
        {
            var a = VehicleDebeziumParser.Parse(Insert).Change!.UpdatedAtMicros;
            var b = VehicleDebeziumParser.Parse(
                Insert.Replace("23:16:04.134788Z", "23:16:04.134789Z")).Change!.UpdatedAtMicros;

            Assert.Equal(1, b - a);
        }
    }
}
