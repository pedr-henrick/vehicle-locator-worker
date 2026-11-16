using VehicleLocator.Application.Positions;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Positions;

public class PositionDeciderTests
{
    [Theory]
    [InlineData(null, null, false, 100L, 10L, PositionDecision.Apply)]
    [InlineData(100L, 10L, false, 200L, 20L, PositionDecision.Apply)]
    [InlineData(200L, 20L, false, 100L, 30L, PositionDecision.IgnoreStale)]
    [InlineData(100L, 10L, false, 100L, 11L, PositionDecision.Apply)]
    [InlineData(100L, 10L, false, 100L, 9L, PositionDecision.IgnoreStale)]
    [InlineData(100L, 10L, false, 100L, 10L, PositionDecision.IgnoreDuplicate)]
    [InlineData(null, null, true, 999L, 99L, PositionDecision.IgnoreDeleted)]
    [InlineData(100L, 10L, true, 999L, 99L, PositionDecision.IgnoreDeleted)]
    public void Decide(
        long? curTs, long? curLsn, bool isDeleted,
        long ts, long lsn,
        PositionDecision expected)
    {
        var current = curTs is null ? null : new PositionViewState(curTs.Value, curLsn!.Value);
        var change = new PositionChange("v-1", -9.6, -35.7, ts, lsn);

        Assert.Equal(expected, PositionDecider.Decide(current, isDeleted, change));
    }
}
