using VehicleLocator.Application.Vehicles;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Vehicles;

public class VehicleDeciderTests
{
    [Theory]
    [InlineData(null, null, false, VehicleChangeKind.Upsert, 100L, 10L, VehicleDecision.Apply)]
    [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 200L, 20L, VehicleDecision.Apply)]
    [InlineData(200L, 20L, false, VehicleChangeKind.Upsert, 100L, 30L, VehicleDecision.IgnoreStale)]
    [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 100L, 11L, VehicleDecision.Apply)]
    [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 100L, 9L, VehicleDecision.IgnoreStale)]
    [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 100L, 10L, VehicleDecision.IgnoreDuplicate)]
    [InlineData(null, null, true, VehicleChangeKind.Upsert, 999L, 99L, VehicleDecision.IgnoreDeleted)]
    [InlineData(200L, 20L, false, VehicleChangeKind.Delete, 100L, 30L, VehicleDecision.Remove)]
    [InlineData(null, null, false, VehicleChangeKind.Delete, 100L, 30L, VehicleDecision.Remove)]
    [InlineData(null, null, true, VehicleChangeKind.Delete, 100L, 30L, VehicleDecision.IgnoreDuplicate)]
    public void Decide(
        long? curTs, long? curLsn, bool isDeleted,
        VehicleChangeKind kind, long ts, long lsn,
        VehicleDecision expected)
    {
        var current = curTs is null ? null : new VehicleViewState("ACTIVE", curTs.Value, curLsn!.Value);
        var change = new VehicleChange("v-1", kind, kind == VehicleChangeKind.Upsert ? "IDLE" : null, ts, lsn);

        Assert.Equal(expected, VehicleDecider.Decide(current, isDeleted, change));
    }
}
