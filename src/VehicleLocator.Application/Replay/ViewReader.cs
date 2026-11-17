using StackExchange.Redis;

namespace VehicleLocator.Application.Replay;

public static class ViewReader
{
    public static async Task<ViewSnapshot> ReadAsync(IConnectionMultiplexer mux, int database)
    {
        var db = mux.GetDatabase(database);
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var entries = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        await foreach (var key in server.KeysAsync(database, "vehicle:*", pageSize: 250))
        {
            var hash = await db.HashGetAllAsync(key);
            if (hash.Length == 0)
                continue;

            entries[(string)key!] = hash.ToDictionary(h => (string)h.Name!, h => (string)h.Value!);
        }

        await foreach (var key in server.KeysAsync(database, "deleted:*", pageSize: 250))
        {
            var value = await db.StringGetAsync(key);
            if (value.IsNull)
                continue;

            entries[(string)key!] = new Dictionary<string, string> { ["value"] = (string)value! };
        }

        return new ViewSnapshot(entries);
    }
}
