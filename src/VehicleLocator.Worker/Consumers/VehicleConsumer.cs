using Microsoft.Extensions.Options;
using VehicleLocator.Application.Vehicles;
using VehicleLocator.Worker.Support.Kafka;

namespace VehicleLocator.Worker.Consumers;

public sealed class VehicleConsumer(ConsumerLoop loop, ApplyVehicleChangeUseCase useCase, IOptions<KafkaOptions> options) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.Run(() =>
            loop.Run(options.Value.VehiclesTopic, options.Value.VehiclesGroupId, useCase.ExecuteAsync, ct)
        , ct);
}
