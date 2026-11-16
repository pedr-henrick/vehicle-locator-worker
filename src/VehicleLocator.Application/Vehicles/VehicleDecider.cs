namespace VehicleLocator.Application.Vehicles;

public sealed record VehicleViewState(string State, long UpdatedAtMicros, long Lsn);

public enum VehicleDecision
{
    Apply,
    Remove,
    IgnoreStale,
    IgnoreDuplicate,
    IgnoreDeleted
}

public static class VehicleDecider
{
    public static VehicleDecision Decide(
        VehicleViewState? current,
        bool isDeleted,
        VehicleChange change)
    {
        if (change.Kind == VehicleChangeKind.Delete)
        {
            return isDeleted ? VehicleDecision.IgnoreDuplicate : VehicleDecision.Remove;
        }

        if (isDeleted)
            return VehicleDecision.IgnoreDeleted;

        if (current is null)
            return VehicleDecision.Apply;

        if (change.UpdatedAtMicros != current.UpdatedAtMicros)
            return change.UpdatedAtMicros > current.UpdatedAtMicros
                ? VehicleDecision.Apply
                : VehicleDecision.IgnoreStale;

        if (change.Lsn != current.Lsn)
            return change.Lsn > current.Lsn
                ? VehicleDecision.Apply
                : VehicleDecision.IgnoreStale;

        return VehicleDecision.IgnoreDuplicate;
    }
}
