using Microsoft.Extensions.Options;
using VehicleLocator.Application.Positions;
using VehicleLocator.Worker.Support.Kafka;

namespace VehicleLocator.Worker.Consumers;

public class PositionConsumer(IConsumerLoop loop, ApplyPositionChangeUseCase useCase, IOptions<KafkaOptions> options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.Run(() => loop.Run(
            options.Value.PositionsTopic,
            options.Value.PositionsGroupId,
            useCase.ExecuteAsync,
            ct), ct);
}
