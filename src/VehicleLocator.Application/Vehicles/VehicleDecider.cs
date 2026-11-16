namespace VehicleLocator.Application.Vehicles
{
    /// <summary>O que está hoje no hash vehicle:{id} (campos do estado).</summary>
    public sealed record VehicleViewState(string State, long UpdatedAtMicros, long Lsn);

    public enum VehicleDecision
    {
        Apply,           // grava state, stateTs, stateLsn
        Remove,          // DEL vehicle:{id} + SET deleted:{id}
        IgnoreStale,
        IgnoreDuplicate,
        IgnoreDeleted    // evento após o delete: premissa de id único violada (logar warning)
    }

    /// <summary>Regra pura, sem I/O.</summary>
    public static class VehicleDecider
    {
        public static VehicleDecision Decide(
            VehicleViewState? current,
            bool isDeleted,
            VehicleChange change)
        {
            if (change.Kind == VehicleChangeKind.Delete)
            {
                // Delete não passa por comparação de versão: o before traz o
                // updated_at antigo, então comparar poderia ignorá-lo por engano.
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
