using VehicleLocator.Application.Positions;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Positions;

public class PositionConvergenceTests
{
    private sealed record Cache(PositionViewState? Pos, bool Deleted);

    private static Cache Fold(Cache cache, PositionChange change) =>
        PositionDecider.Decide(cache.Pos, cache.Deleted, change) switch
        {
            PositionDecision.Apply => cache with { Pos = new PositionViewState(change.RecordedAtMicros, change.Lsn) },
            _ => cache
        };

    private static Cache Run(IEnumerable<PositionChange> events, bool deleted = false) =>
        events.Aggregate(new Cache(null, deleted), Fold);

    private static PositionChange P(long ts, long lsn) => new("v-1", -9.6, -35.7, ts, lsn);

    private static readonly List<PositionChange> Positions =
    [
        P(100, 10),
        P(300, 20),
        P(200, 30),
        P(300, 40),
        P(50, 50)
    ];

    [Fact]
    public void Any_arrival_order_converges_to_the_latest_position()
    {
        foreach (var order in TestPermutations.Of(Positions))
        {
            var cache = Run(order);

            Assert.Equal(new PositionViewState(300, 40), cache.Pos);
        }
    }

    [Fact]
    public void Redelivering_the_whole_stream_changes_nothing()
    {
        foreach (var order in TestPermutations.Of(Positions))
            Assert.Equal(Run(order), Run(order.Concat(order)));
    }

    [Fact]
    public void Every_position_is_ignored_once_the_vehicle_is_deleted()
    {
        foreach (var order in TestPermutations.Of(Positions))
        {
            var cache = Run(order, deleted: true);

            Assert.Null(cache.Pos);
        }
    }
}
