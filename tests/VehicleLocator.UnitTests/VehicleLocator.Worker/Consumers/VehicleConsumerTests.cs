using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using VehicleLocator.Application.Shared;
using VehicleLocator.Application.Vehicles;
using VehicleLocator.Worker.Consumers;
using VehicleLocator.Worker.Support.Kafka;

namespace VehicleLocator.UnitTests.VehicleLocator.Worker;

public class VehicleConsumerTests
{
    [Fact]
    public async Task Runs_the_loop_on_the_vehicles_topic_and_group_wired_to_the_vehicle_use_case()
    {
        var loop = Substitute.For<IConsumerLoop>();
        MessageHandler? captured = null;
        loop.When(l => l.Run(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageHandler>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.ArgAt<MessageHandler>(2));

        var options = Options.Create(new KafkaOptions { VehiclesTopic = "t-vehicles", VehiclesGroupId = "g-vehicles" });
        var writer = Substitute.For<VehicleViewWriter>(Substitute.For<IConnectionMultiplexer>());
        var useCase = new ApplyVehicleChangeUseCase(writer, NullLogger<ApplyVehicleChangeUseCase>.Instance);

        using var consumer = new VehicleConsumer(loop, useCase, options);

        await consumer.StartAsync(CancellationToken.None);
        await consumer.ExecuteTask!;

        loop.Received(1).Run("t-vehicles", "g-vehicles", Arg.Any<MessageHandler>(), Arg.Any<CancellationToken>());

        Assert.NotNull(captured);
        Assert.Equal(ProjectionResult.IgnoredTombstone, await captured!("k", null, CancellationToken.None));

        await consumer.StopAsync(CancellationToken.None);
    }
}
