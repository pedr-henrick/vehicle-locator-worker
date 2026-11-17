using VehicleLocator.Application.Positions;
using VehicleLocator.Application.Vehicles;
using VehicleLocator.UnitTests.Support.Helpers;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Positions;

public class CrossStreamConvergenceTests
{
    private sealed record Cache(VehicleViewState? State, PositionViewState? Pos, bool Deleted);

    private static Func<Cache, Cache> V(VehicleChange c) => cache =>
        VehicleDecider.Decide(cache.State, cache.Deleted, c) switch
        {
            VehicleDecision.Apply => cache with { State = new VehicleViewState(c.State!, c.UpdatedAtMicros, c.Lsn) },
            VehicleDecision.Remove => new Cache(null, null, true),
            _ => cache
        };

    private static Func<Cache, Cache> P(PositionChange c) => cache =>
        PositionDecider.Decide(cache.Pos, cache.Deleted, c) switch
        {
            PositionDecision.Apply => cache with { Pos = new PositionViewState(c.RecordedAtMicros, c.Lsn) },
            _ => cache
        };

    private static Cache Run(IEnumerable<Func<Cache, Cache>> events) =>
        events.Aggregate(new Cache(null, null, false), (cache, apply) => apply(cache));

    private static VehicleChange Upsert(string state, long ts, long lsn) =>
        new("v-1", VehicleChangeKind.Upsert, state, ts, lsn);

    private static VehicleChange Delete(long ts, long lsn) =>
        new("v-1", VehicleChangeKind.Delete, null, ts, lsn);

    private static PositionChange Pos(long ts, long lsn) => new("v-1", -9.6, -35.7, ts, lsn);

    private static List<Func<Cache, Cache>> Live() =>
    [
        V(Upsert("REGISTERED", 100, 10)),
        V(Upsert("ACTIVE", 200, 20)),
        P(Pos(100, 15)),
        P(Pos(300, 25)),
        P(Pos(200, 35))
    ];

    [Fact]
    public void Any_interleaving_of_both_topics_converges_to_the_same_view()
    {
        foreach (var order in TestPermutations.Of(Live()))
        {
            var cache = Run(order);

            Assert.False(cache.Deleted);
            Assert.Equal(new VehicleViewState("ACTIVE", 200, 20), cache.State);
            Assert.Equal(new PositionViewState(300, 25), cache.Pos);
        }
    }

    [Fact]
    public void Redelivering_both_streams_changes_nothing()
    {
        foreach (var order in TestPermutations.Of(Live()))
            Assert.Equal(Run(order), Run(order.Concat(order)));
    }

    [Fact]
    public void Delete_wins_in_any_interleaving_and_leaves_no_residue()
    {
        var all = Live();
        all.Add(V(Delete(ts: 50, lsn: 60)));

        foreach (var order in TestPermutations.Of(all))
        {
            var cache = Run(order);

            Assert.True(cache.Deleted);
            Assert.Null(cache.State);
            Assert.Null(cache.Pos);
        }
    }

    [Fact]
    public void Position_before_vehicle_leaves_state_unknown_until_the_vehicle_arrives()
    {
        var afterPosition = Run([P(Pos(100, 15))]);

        Assert.Null(afterPosition.State);
        Assert.Equal(new PositionViewState(100, 15), afterPosition.Pos);

        var afterVehicle = Run([P(Pos(100, 15)), V(Upsert("REGISTERED", 100, 10))]);

        Assert.Equal(new VehicleViewState("REGISTERED", 100, 10), afterVehicle.State);
        Assert.Equal(new PositionViewState(100, 15), afterVehicle.Pos);
    }
}
