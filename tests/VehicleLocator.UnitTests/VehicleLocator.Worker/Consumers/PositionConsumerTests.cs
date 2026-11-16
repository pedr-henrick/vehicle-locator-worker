using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using VehicleLocator.Application.Positions;
using VehicleLocator.Application.Shared;
using VehicleLocator.Worker;
using VehicleLocator.Worker.Support.Kafka;

namespace VehicleLocator.UnitTests.VehicleLocator.Worker.Consumers;

public class PositionConsumerTests
{
    [Fact]
    public async Task Runs_the_loop_on_the_positions_topic_and_group_wired_to_the_position_use_case()
    {
        var loop = Substitute.For<IConsumerLoop>();
        MessageHandler? captured = null;
        loop.When(l => l.Run(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageHandler>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.ArgAt<MessageHandler>(2));

        var options = Options.Create(new KafkaOptions { PositionsTopic = "t-positions", PositionsGroupId = "g-positions" });
        var writer = Substitute.For<PositionViewWriter>(Substitute.For<IConnectionMultiplexer>());
        var useCase = new ApplyPositionChangeUseCase(writer, NullLogger<ApplyPositionChangeUseCase>.Instance);

        using var consumer = new PositionConsumer(loop, useCase, options);

        await consumer.StartAsync(CancellationToken.None);
        await consumer.ExecuteTask!;

        loop.Received(1).Run("t-positions", "g-positions", Arg.Any<MessageHandler>(), Arg.Any<CancellationToken>());

        Assert.NotNull(captured);
        Assert.Equal(ProjectionResult.IgnoredTombstone, await captured!("k", null, CancellationToken.None));

        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Vehicles_and_positions_use_different_topics_and_consumer_groups()
    {
        var options = new KafkaOptions();

        Assert.NotEqual(options.VehiclesTopic, options.PositionsTopic);
        Assert.NotEqual(options.VehiclesGroupId, options.PositionsGroupId);
    }
}
