namespace VehicleLocator.Application.Vehicles
{
    /// <summary>
    /// Represents a vehicle's current view state and its update metadata.
    /// </summary>
    /// <param name="State">The vehicle's state.</param>
    /// <param name="UpdatedAtMicros">The time of the update, in microseconds.</param>
    /// <param name="Lsn">The log sequence number associated with the update.</param>
    public sealed record VehicleViewState(
        string State,
        long UpdatedAtMicros,
        long Lsn);

    public enum VehicleDecision
    {
        Apply,
        Remove,
        IgnoreStale,
        IgnoreDuplicate,
        IgnoreDeleted
    }

    /// <summary>
    /// Determines whether a vehicle change should be applied, removed, or ignored.
    /// </summary>
    /// <remarks>Non-delete changes are ordered by update timestamp and then log sequence number. Delete
    /// changes bypass version comparison.</remarks>
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
}
