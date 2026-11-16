using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using VehicleLocator.Application.Positions;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Positions;

public class ApplyPositionChangeUseCaseTests
{
    private const string Insert = """
        {"before":null,"after":{"id":1,"vehicle_id":"v-1","lat":-9.66,"lng":-35.73,"recorded_at":"2026-11-15T23:16:09.214383Z"},"source":{"snapshot":"false","lsn":26812760},"op":"c"}
        """;

    private readonly PositionViewWriter _writer =
        Substitute.For<PositionViewWriter>(Substitute.For<IConnectionMultiplexer>());

    private ApplyPositionChangeUseCase NewUseCase() =>
        new(_writer, NullLogger<ApplyPositionChangeUseCase>.Instance);

    [Fact]
    public async Task Tombstone_is_ignored_and_the_writer_is_never_called()
    {
        var result = await NewUseCase().ExecuteAsync("""{"vehicle_id":"v-1"}""", null, CancellationToken.None);

        Assert.Equal(ProjectionResult.IgnoredTombstone, result);
        await _writer.DidNotReceive().ApplyAsync(Arg.Any<PositionChange>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"op":"d","source":{"lsn":1}}""")]
    public async Task Garbage_is_invalid_and_the_writer_is_never_called(string value)
    {
        var result = await NewUseCase().ExecuteAsync("k", value, CancellationToken.None);

        Assert.Equal(ProjectionResult.Invalid, result);
        await _writer.DidNotReceive().ApplyAsync(Arg.Any<PositionChange>());
    }

    [Fact]
    public async Task Insert_is_parsed_and_handed_to_the_writer()
    {
        _writer.ApplyAsync(Arg.Any<PositionChange>()).Returns(PositionDecision.Apply);

        var result = await NewUseCase().ExecuteAsync("k", Insert, CancellationToken.None);

        Assert.Equal(ProjectionResult.Applied, result);
        await _writer.Received(1).ApplyAsync(Arg.Is<PositionChange>(c =>
            c.VehicleId == "v-1"
            && c.Lat == -9.66
            && c.Lng == -35.73
            && c.Lsn == 26812760
            && c.RecordedAtMicros == 1794784569214383L));
    }

    [Theory]
    [InlineData(PositionDecision.Apply, ProjectionResult.Applied)]
    [InlineData(PositionDecision.IgnoreStale, ProjectionResult.IgnoredStale)]
    [InlineData(PositionDecision.IgnoreDuplicate, ProjectionResult.IgnoredDuplicate)]
    [InlineData(PositionDecision.IgnoreDeleted, ProjectionResult.IgnoredDeleted)]
    public async Task Writer_decision_is_mapped_to_the_projection_result(
        PositionDecision decision, ProjectionResult expected)
    {
        _writer.ApplyAsync(Arg.Any<PositionChange>()).Returns(decision);

        var result = await NewUseCase().ExecuteAsync("k", Insert, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Writer_failure_propagates_so_the_consumer_does_not_commit_the_offset()
    {
        _writer.ApplyAsync(Arg.Any<PositionChange>())
            .ThrowsAsync(new OptimisticWriteExhaustedException(5));

        await Assert.ThrowsAsync<OptimisticWriteExhaustedException>(() =>
            NewUseCase().ExecuteAsync("k", Insert, CancellationToken.None));
    }

    [Fact]
    public void Every_decision_value_has_a_mapping()
    {
        foreach (var decision in Enum.GetValues<PositionDecision>())
            Assert.NotEqual(ProjectionResult.Invalid, ApplyPositionChangeUseCase.ToResult(decision));
    }
}
