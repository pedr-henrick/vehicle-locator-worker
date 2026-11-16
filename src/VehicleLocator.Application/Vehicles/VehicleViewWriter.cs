using StackExchange.Redis;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Application.Vehicles;

public class VehicleViewWriter(IConnectionMultiplexer mux)
{
    public virtual Task<VehicleDecision> ApplyAsync(VehicleChange change) =>
        OptimisticWrite.RunAsync(() => TryApplyAsync(change));

    private async Task<(bool Committed, VehicleDecision Result)> TryApplyAsync(VehicleChange change)
    {
        var db = mux.GetDatabase();
        var vehicleKey = ValkeyKeys.Vehicle(change.VehicleId);
        var deletedKey = ValkeyKeys.Deleted(change.VehicleId);

        var isDeleted = await db.KeyExistsAsync(deletedKey);
        var fields = await db.HashGetAsync(vehicleKey,
            [ValkeyKeys.StateTs, ValkeyKeys.StateLsn, ValkeyKeys.State]);

        VehicleViewState? current = fields[0].IsNull
            ? null
            : new VehicleViewState((string)fields[2]!, (long)fields[0], (long)fields[1]);

        var decision = VehicleDecider.Decide(current, isDeleted, change);

        if (decision is not (VehicleDecision.Apply or VehicleDecision.Remove))
            return (true, decision);

        var tran = db.CreateTransaction();

        tran.AddCondition(isDeleted
            ? Condition.KeyExists(deletedKey)
            : Condition.KeyNotExists(deletedKey));

        if (decision == VehicleDecision.Apply)
        {
            if (current is null)
            {
                tran.AddCondition(Condition.HashNotExists(vehicleKey, ValkeyKeys.StateTs));
            }
            else
            {
                tran.AddCondition(Condition.HashEqual(vehicleKey, ValkeyKeys.StateTs, current.UpdatedAtMicros));
                tran.AddCondition(Condition.HashEqual(vehicleKey, ValkeyKeys.StateLsn, current.Lsn));
            }

            _ = tran.HashSetAsync(vehicleKey,
            [
                new HashEntry(ValkeyKeys.State, change.State!),
                new HashEntry(ValkeyKeys.StateTs, change.UpdatedAtMicros),
                new HashEntry(ValkeyKeys.StateLsn, change.Lsn)
            ]);
        }
        else
        {
            _ = tran.KeyDeleteAsync(vehicleKey);
            _ = tran.StringSetAsync(deletedKey, "1");
        }

        var committed = await tran.ExecuteAsync();
        return (committed, decision);
    }
}
