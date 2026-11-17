using VehicleLocator.MockWriters;
using Xunit;

namespace VehicleLocator.UnitTests.VehicleLocator.MockWriters;

public class ScenariosTests
{
    [Fact]
    public void IdFor_PreAndPostShareTheSameVehicleId()
    {
        Assert.Equal(
            Scenarios.IdFor("p", "snapshot-then-stream-pre"),
            Scenarios.IdFor("p", "snapshot-then-stream-post"));
        Assert.Equal("p-snapshot-then-stream-1", Scenarios.IdFor("p", "snapshot-then-stream-pre"));
    }

    [Theory]
    [InlineData("lifecycle")]
    [InlineData("position-before-vehicle")]
    [InlineData("snapshot-only")]
    public void IdFor_ScenarioWithoutSuffix_KeepsItsName(string scenario)
    {
        Assert.Equal($"p-{scenario}-1", Scenarios.IdFor("p", scenario));
    }

    [Fact]
    public void IdFor_DifferentScenarios_NeverCollide()
    {
        var ids = Scenarios.Checked.Select(s => Scenarios.IdFor("p", s)).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ForGroup_UnknownGroup_ReturnsNull()
    {
        Assert.Null(Scenarios.ForGroup("xyz"));
    }

    [Fact]
    public void ForGroup_All_CoversEveryRunnableScenario()
    {
        var all = Scenarios.ForGroup("all")!;

        Assert.Equal(Scenarios.All.Length, all.Length);
        Assert.Contains("snapshot-then-stream-pre", all);
        Assert.Contains("snapshot-then-stream-post", all);
    }

    [Fact]
    public void VolumeOrder_IsAPermutationOfAllIndexes()
    {
        var indexes = Enumerable.Range(0, Scenarios.VolumeCount)
            .Select(k => k * 37 % Scenarios.VolumeCount)
            .ToList();

        Assert.Equal(Enumerable.Range(0, Scenarios.VolumeCount), indexes.OrderBy(i => i));
    }
}
