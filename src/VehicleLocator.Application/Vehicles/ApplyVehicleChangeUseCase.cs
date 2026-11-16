using Microsoft.Extensions.Logging;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Application.Vehicles;

public sealed class ApplyVehicleChangeUseCase(VehicleViewWriter writer, ILogger<ApplyVehicleChangeUseCase> logger)
{
    public async Task<ProjectionResult> ExecuteAsync(string? key, string? value, CancellationToken ct)
    {
        var parsed = VehicleDebeziumParser.Parse(value);

        if (parsed.Kind == ParseKind.Tombstone)
            return ProjectionResult.IgnoredTombstone;

        if (parsed.Kind == ParseKind.Invalid)
        {
            logger.LogWarning("Invalid vehicle event key={Key}: {Error}", key, parsed.Error);
            return ProjectionResult.Invalid;
        }

        var change = parsed.Change!;
        var decision = await writer.ApplyAsync(change);

        if (decision == VehicleDecision.IgnoreDeleted)
            logger.LogWarning(
                "Event for deleted vehicle {VehicleId} ignored (unique-id assumption violated)",
                change.VehicleId);

        logger.LogInformation("Vehicle {VehicleId} {Kind} lsn={Lsn} -> {Decision}",
            change.VehicleId, change.Kind, change.Lsn, decision);

        return decision switch
        {
            VehicleDecision.Apply or VehicleDecision.Remove => ProjectionResult.Applied,
            VehicleDecision.IgnoreStale => ProjectionResult.IgnoredStale,
            VehicleDecision.IgnoreDuplicate => ProjectionResult.IgnoredDuplicate,
            VehicleDecision.IgnoreDeleted => ProjectionResult.IgnoredDeleted,
            _ => ProjectionResult.Invalid
        };
    }
}
