using VehicleLocator.MockWriters;
using Xunit;

namespace VehicleLocator.UnitTests.VehicleLocator.MockWriters;

public class StreamPlanTests
{
    private const string Prefix = "t";

    private static StreamOptions With(int ooo, int dup, int seed = 42) =>
        StreamOptions.Default with { OutOfOrderPercent = ooo, DuplicatePercent = dup, Seed = seed };

    [Fact]
    public void Build_SameSeed_ProducesTheSamePlan()
    {
        var first = StreamPlan.Build(Prefix, With(15, 10));
        var second = StreamPlan.Build(Prefix, With(15, 10));

        Assert.True(first.SequenceEqual(second));
    }

    [Fact]
    public void Build_StartsWithOneVehicleInsertPerVehicle()
    {
        var options = With(15, 10);
        var plan = StreamPlan.Build(Prefix, options);

        var head = plan.Take(options.Vehicles).ToList();
        Assert.All(head, op => Assert.Equal(StreamOpKind.InsertVehicle, op.Kind));
        Assert.Equal(options.Vehicles, head.Select(op => op.VehicleId).Distinct().Count());
    }

    [Fact]
    public void Build_WithoutDisturbance_KeepsTimestampOrderPerVehicle()
    {
        var plan = StreamPlan.Build(Prefix, With(0, 0));

        foreach (var group in plan.Where(op => op.Kind == StreamOpKind.InsertPosition).GroupBy(op => op.VehicleId))
        {
            var timestamps = group.Select(op => op.Timestamp).ToList();
            Assert.Equal(timestamps.OrderBy(t => t).ToList(), timestamps);
        }
    }

    [Fact]
    public void Build_WithFullOutOfOrder_DiffersFromTheOrderedPlan()
    {
        var ordered = StreamPlan.Build(Prefix, With(0, 0));
        var disordered = StreamPlan.Build(Prefix, With(100, 0));

        Assert.False(ordered.SequenceEqual(disordered));
        Assert.Equal(ordered.Count, disordered.Count);
    }

    [Fact]
    public void Build_NoDuplicates_HasExactlyOneOperationPerEvent()
    {
        var options = With(15, 0);
        var plan = StreamPlan.Build(Prefix, options);

        var expected = options.Vehicles * (1 + (options.Transitions - 1) + options.Positions);
        Assert.Equal(expected, plan.Count);
    }

    [Fact]
    public void Build_FullDuplicates_RepeatsEveryOperationExceptVehicleInserts()
    {
        var options = With(15, 100);
        var plan = StreamPlan.Build(Prefix, options);

        var expected = options.Vehicles * (1 + 2 * ((options.Transitions - 1) + options.Positions));
        Assert.Equal(expected, plan.Count);
    }

    [Fact]
    public void Build_EveryPositionAndTransitionAppearsAtLeastOnce()
    {
        var options = With(15, 10);
        var plan = StreamPlan.Build(Prefix, options);

        foreach (var group in plan.GroupBy(op => op.VehicleId))
        {
            var positions = group.Where(op => op.Kind == StreamOpKind.InsertPosition)
                .Select(op => op.Timestamp).Distinct().Count();
            var updates = group.Where(op => op.Kind == StreamOpKind.UpdateVehicle)
                .Select(op => op.Timestamp).Distinct().Count();

            Assert.Equal(options.Positions, positions);
            Assert.Equal(options.Transitions - 1, updates);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 10)]
    [InlineData(100, 100)]
    public void Build_ExpectedFinalValuesDoNotDependOnTheDisturbance(int ooo, int dup)
    {
        var options = With(ooo, dup);
        var plan = StreamPlan.Build(Prefix, options);

        foreach (var group in plan.GroupBy(op => op.VehicleId))
        {
            var lastPosition = group.Where(op => op.Kind == StreamOpKind.InsertPosition)
                .MaxBy(op => op.Timestamp)!;
            var lastState = group.Where(op => op.Kind != StreamOpKind.InsertPosition)
                .MaxBy(op => op.Timestamp)!;

            Assert.Equal(StreamPlan.LatAt(options.Positions - 1), lastPosition.Lat);
            Assert.Equal(StreamPlan.LngAt(options.Positions - 1), lastPosition.Lng);
            Assert.Equal(StreamPlan.StateAt(options.Transitions - 1), lastState.State);
        }
    }
}
