using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VehicleLocator.Application.Shared;
using VehicleLocator.Application.Vehicles;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Vehicles;

public class ApplyVehicleChangeUseCaseTests
{
    private const string Insert = """
        {"before":null,"after":{"id":"v-1","state":"REGISTERED","updated_at":"2026-11-15T23:16:04.134788Z"},"source":{"snapshot":"false","lsn":26812288},"op":"c"}
        """;

    private const string Delete = """
        {"before":{"id":"v-1","state":"ACTIVE","updated_at":"2026-11-15T23:20:49.643391Z"},"after":null,"source":{"snapshot":"false","lsn":26814408},"op":"d"}
        """;

    private readonly IVehicleViewWriter _writer = Substitute.For<IVehicleViewWriter>();

    private ApplyVehicleChangeUseCase NewUseCase() =>
        new(_writer, NullLogger<ApplyVehicleChangeUseCase>.Instance);

    [Fact]
    public async Task Tombstone_is_ignored_and_the_writer_is_never_called()
    {
        var result = await NewUseCase().ExecuteAsync("""{"id":"v-1"}""", null, CancellationToken.None);

        Assert.Equal(ProjectionResult.IgnoredTombstone, result);
        await _writer.DidNotReceive().ApplyAsync(Arg.Any<VehicleChange>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"op":"x","source":{"lsn":1}}""")]
    public async Task Garbage_is_invalid_and_the_writer_is_never_called(string value)
    {
        var result = await NewUseCase().ExecuteAsync("k", value, CancellationToken.None);

        Assert.Equal(ProjectionResult.Invalid, result);
        await _writer.DidNotReceive().ApplyAsync(Arg.Any<VehicleChange>());
    }

    [Fact]
    public async Task Insert_is_parsed_and_handed_to_the_writer()
    {
        _writer.ApplyAsync(Arg.Any<VehicleChange>()).Returns(VehicleDecision.Apply);

        var result = await NewUseCase().ExecuteAsync("k", Insert, CancellationToken.None);

        Assert.Equal(ProjectionResult.Applied, result);
        await _writer.Received(1).ApplyAsync(Arg.Is<VehicleChange>(c =>
            c.VehicleId == "v-1"
            && c.Kind == VehicleChangeKind.Upsert
            && c.State == "REGISTERED"
            && c.Lsn == 26812288
            && c.UpdatedAtMicros == 1794784564134788L));
    }

    [Fact]
    public async Task Delete_is_handed_to_the_writer_as_a_delete()
    {
        _writer.ApplyAsync(Arg.Any<VehicleChange>()).Returns(VehicleDecision.Remove);

        var result = await NewUseCase().ExecuteAsync("k", Delete, CancellationToken.None);

        Assert.Equal(ProjectionResult.Applied, result);
        await _writer.Received(1).ApplyAsync(Arg.Is<VehicleChange>(c =>
            c.Kind == VehicleChangeKind.Delete && c.VehicleId == "v-1" && c.State == null));
    }

    [Theory]
    [InlineData(VehicleDecision.Apply, ProjectionResult.Applied)]
    [InlineData(VehicleDecision.Remove, ProjectionResult.Applied)]
    [InlineData(VehicleDecision.IgnoreStale, ProjectionResult.IgnoredStale)]
    [InlineData(VehicleDecision.IgnoreDuplicate, ProjectionResult.IgnoredDuplicate)]
    [InlineData(VehicleDecision.IgnoreDeleted, ProjectionResult.IgnoredDeleted)]
    public async Task Writer_decision_is_mapped_to_the_projection_result(
        VehicleDecision decision, ProjectionResult expected)
    {
        _writer.ApplyAsync(Arg.Any<VehicleChange>()).Returns(decision);

        var result = await NewUseCase().ExecuteAsync("k", Insert, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Writer_failure_propagates_so_the_consumer_does_not_commit_the_offset()
    {
        _writer.ApplyAsync(Arg.Any<VehicleChange>())
            .ThrowsAsync(new OptimisticWriteExhaustedException(5));

        await Assert.ThrowsAsync<OptimisticWriteExhaustedException>(() =>
            NewUseCase().ExecuteAsync("k", Insert, CancellationToken.None));
    }

    [Fact]
    public void Every_decision_value_has_a_mapping()
    {
        foreach (var decision in Enum.GetValues<VehicleDecision>())
            Assert.NotEqual(ProjectionResult.Invalid, ApplyVehicleChangeUseCase.ToResult(decision));
    }
}
