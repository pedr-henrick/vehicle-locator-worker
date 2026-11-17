namespace VehicleLocator.Application.Positions;

public sealed record PositionViewState(long RecordedAtMicros, long Lsn);

public enum PositionDecision
{
    Apply,
    IgnoreStale,
    IgnoreDuplicate,
    IgnoreDeleted
}

public static class PositionDecider
{
    public static PositionDecision Decide(PositionViewState? current, bool isDeleted, PositionChange change)
    {
        if (isDeleted)
            return PositionDecision.IgnoreDeleted;

        if (current is null)
            return PositionDecision.Apply;

        if (change.RecordedAtMicros != current.RecordedAtMicros)
            return change.RecordedAtMicros > current.RecordedAtMicros
                ? PositionDecision.Apply
                : PositionDecision.IgnoreStale;

        if (change.Lsn != current.Lsn)
            return change.Lsn > current.Lsn
                ? PositionDecision.Apply
                : PositionDecision.IgnoreStale;

        return PositionDecision.IgnoreDuplicate;
    }
}
