using Microsoft.Extensions.Logging;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Application.Positions;

public class ApplyPositionChangeUseCase(
    PositionViewWriter writer,
    ILogger<ApplyPositionChangeUseCase> logger)
{
    public async Task<ProjectionResult> ExecuteAsync(string? key, string? value, CancellationToken ct)
    {
        var parsed = PositionDebeziumParser.Parse(value);

        if (parsed.Kind == ParseKind.Tombstone)
            return ProjectionResult.IgnoredTombstone;

        if (parsed.Kind == ParseKind.Invalid)
        {
            logger.LogWarning("Invalid position event key={Key}: {Error}", key, parsed.Error);
            return ProjectionResult.Invalid;
        }

        var change = parsed.Change!;
        var decision = await writer.ApplyAsync(change);

        logger.LogInformation("Position {VehicleId} recordedAt={RecordedAt} lsn={Lsn} -> {Decision}",
            change.VehicleId, change.RecordedAtMicros, change.Lsn, decision);

        return ToResult(decision);
    }

    public static ProjectionResult ToResult(PositionDecision decision) => decision switch
    {
        PositionDecision.Apply => ProjectionResult.Applied,
        PositionDecision.IgnoreStale => ProjectionResult.IgnoredStale,
        PositionDecision.IgnoreDuplicate => ProjectionResult.IgnoredDuplicate,
        PositionDecision.IgnoreDeleted => ProjectionResult.IgnoredDeleted,
        _ => ProjectionResult.Invalid
    };
}
