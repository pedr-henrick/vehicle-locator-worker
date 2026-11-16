using StackExchange.Redis;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Application.Positions;

public class PositionViewWriter(IConnectionMultiplexer mux)
{
    public virtual Task<PositionDecision> ApplyAsync(PositionChange change) => OptimisticWrite.RunAsync(() => TryApplyAsync(change));

    private async Task<(bool Committed, PositionDecision Result)> TryApplyAsync(PositionChange change)
    {
        var db = mux.GetDatabase();
        var vehicleKey = ValkeyKeys.Vehicle(change.VehicleId);
        var deletedKey = ValkeyKeys.Deleted(change.VehicleId);

        var isDeleted = await db.KeyExistsAsync(deletedKey);
        var fields = await db.HashGetAsync(vehicleKey, [ValkeyKeys.PosTs, ValkeyKeys.PosLsn]);

        PositionViewState? current = fields[0].IsNull
            ? null
            : new PositionViewState((long)fields[0], (long)fields[1]);

        var decision = PositionDecider.Decide(current, isDeleted, change);

        if (decision != PositionDecision.Apply)
            return (true, decision);

        var tran = db.CreateTransaction();

        tran.AddCondition(Condition.KeyNotExists(deletedKey));

        if (current is null)
        {
            tran.AddCondition(Condition.HashNotExists(vehicleKey, ValkeyKeys.PosTs));
        }
        else
        {
            tran.AddCondition(Condition.HashEqual(vehicleKey, ValkeyKeys.PosTs, current.RecordedAtMicros));
            tran.AddCondition(Condition.HashEqual(vehicleKey, ValkeyKeys.PosLsn, current.Lsn));
        }

        _ = tran.HashSetAsync(vehicleKey,
        [
            new HashEntry(ValkeyKeys.Lat, change.Lat),
            new HashEntry(ValkeyKeys.Lng, change.Lng),
            new HashEntry(ValkeyKeys.PosTs, change.RecordedAtMicros),
            new HashEntry(ValkeyKeys.PosLsn, change.Lsn)
        ]);

        var committed = await tran.ExecuteAsync();
        return (committed, decision);
    }
}
