using VehicleLocator.Application.Vehicles;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Vehicles;

public class VehicleConvergenceTests
{
    private sealed record Cache(VehicleViewState? State, bool Deleted);

    private static Cache Fold(Cache cache, VehicleChange change) =>
        VehicleDecider.Decide(cache.State, cache.Deleted, change) switch
        {
            VehicleDecision.Apply => new Cache(
                new VehicleViewState(change.State!, change.UpdatedAtMicros, change.Lsn), false),
            VehicleDecision.Remove => new Cache(null, true),
            _ => cache
        };

    private static Cache Run(IEnumerable<VehicleChange> events) =>
        events.Aggregate(new Cache(null, false), Fold);

    private static VehicleChange Upsert(string state, long ts, long lsn) =>
        new("v-1", VehicleChangeKind.Upsert, state, ts, lsn);

    private static VehicleChange Delete(long ts, long lsn) =>
        new("v-1", VehicleChangeKind.Delete, null, ts, lsn);

    private static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1)
        {
            yield return [.. items];
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, idx) => idx != i).ToList();
            foreach (var p in Permutations(rest))
            {
                p.Insert(0, items[i]);
                yield return p;
            }
        }
    }

    private static readonly List<VehicleChange> Upserts =
    [
        Upsert("REGISTERED", 100, 10),
        Upsert("ACTIVE", 200, 20),
        Upsert("IDLE", 150, 30),
        Upsert("MAINTENANCE", 300, 40),
        Upsert("IDLE", 300, 50)
    ];

    [Fact]
    public void Any_arrival_order_converges_to_the_same_state()
    {
        foreach (var order in Permutations(Upserts))
        {
            var cache = Run(order);

            Assert.False(cache.Deleted);
            Assert.Equal(new VehicleViewState("IDLE", 300, 50), cache.State);
        }
    }

    [Fact]
    public void Redelivering_the_whole_stream_changes_nothing()
    {
        foreach (var order in Permutations(Upserts))
        {
            var once = Run(order);
            var twice = Run(order.Concat(order));

            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void Delete_wins_in_any_order_and_leaves_no_state()
    {
        var all = Upserts.Append(Delete(ts: 50, lsn: 60)).ToList();

        foreach (var order in Permutations(all))
        {
            var cache = Run(order);

            Assert.True(cache.Deleted);
            Assert.Null(cache.State);
        }
    }

    [Fact]
    public void Redelivered_delete_is_a_no_op()
    {
        var once = Run([Upsert("ACTIVE", 100, 10), Delete(100, 20)]);
        var twice = Run([Upsert("ACTIVE", 100, 10), Delete(100, 20), Delete(100, 20)]);

        Assert.Equal(once, twice);
    }
}
