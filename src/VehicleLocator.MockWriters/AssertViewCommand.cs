using System.Globalization;
using StackExchange.Redis;

namespace VehicleLocator.MockWriters;

// Compara a view do Valkey com os valores esperados de cada cenário.
// Faz polling até convergir ou estourar o timeout.
// Códigos de saída: 0 = OK, 1 = divergência.
public static class AssertViewCommand
{
    private const double Tolerance = 1e-9;

    public static async Task<int> RunAsync(string[] args)
    {
        var prefix = args.Length > 0 ? args[0] : "e2e";
        var timeoutSeconds = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 120;
        var database = args.Length > 2 && int.TryParse(args[2], out var d) ? d : 0;

        var options = ConfigurationOptions.Parse(
            Environment.GetEnvironmentVariable("VALKEY") ?? "localhost:6379");
        options.DefaultDatabase = database;
        options.AbortOnConnectFail = false;

        await using var mux = await ConnectionMultiplexer.ConnectAsync(options);
        var redis = mux.GetDatabase();

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        List<string> failures;
        while (true)
        {
            failures = await CheckAllAsync(redis, prefix);
            if (failures.Count == 0)
            {
                Console.WriteLine($"OK ({Scenarios.All.Length} cenários, prefixo '{prefix}', db {database})");
                return 0;
            }

            if (DateTime.UtcNow >= deadline)
                break;

            await Task.Delay(2000);
        }

        Console.WriteLine($"FALHA: a view não bate com o esperado (prefixo '{prefix}', db {database}):");
        foreach (var f in failures)
            Console.WriteLine($"  - {f}");
        return 1;
    }

    private static async Task<List<string>> CheckAllAsync(IDatabase db, string prefix)
    {
        var failures = new List<string>();
        var last = Scenarios.VolumeCount - 1;

        foreach (var scenario in Scenarios.All)
        {
            var id = Scenarios.IdFor(prefix, scenario);
            switch (scenario)
            {
                case "lifecycle":
                    await ExpectPresent(failures, db, scenario, id, "MAINTENANCE", null, null);
                    break;
                case "out-of-order":
                    await ExpectPresent(failures, db, scenario, id, "ACTIVE", null, null);
                    break;
                case "delete":
                case "position-after-delete":
                    await ExpectDeleted(failures, db, scenario, id);
                    break;
                case "volume":
                    await ExpectPresent(failures, db, scenario, id, "REGISTERED",
                        Scenarios.VolumeLat(last), Scenarios.VolumeLng(last));
                    break;
                case "positions":
                    await ExpectPresent(failures, db, scenario, id, "REGISTERED", -9.62, -35.72);
                    break;
                case "position-out-of-order":
                    await ExpectPresent(failures, db, scenario, id, "REGISTERED", -9.70, -35.70);
                    break;
                case "position-duplicate":
                case "position-before-vehicle":
                    await ExpectPresent(failures, db, scenario, id, "REGISTERED", -9.60, -35.70);
                    break;
                default:
                    failures.Add($"{scenario}: sem expectativa cadastrada");
                    break;
            }
        }

        return failures;
    }

    private static async Task ExpectPresent(
        List<string> failures, IDatabase db, string scenario, string id,
        string? state, double? lat, double? lng)
    {
        var key = $"vehicle:{id}";
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0)
        {
            failures.Add($"{scenario}: {key} ausente");
            return;
        }

        if (await db.KeyExistsAsync($"deleted:{id}"))
            failures.Add($"{scenario}: marcador deleted:{id} não deveria existir");

        var hash = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());

        if (state is not null && (!hash.TryGetValue("state", out var actual) || actual != state))
            failures.Add($"{scenario}: state esperado '{state}', atual '{(hash.GetValueOrDefault("state") ?? "ausente")}'");

        if (lat is not null) CheckNumber(failures, scenario, hash, "lat", lat.Value);
        if (lng is not null) CheckNumber(failures, scenario, hash, "lng", lng.Value);
    }

    private static async Task ExpectDeleted(List<string> failures, IDatabase db, string scenario, string id)
    {
        if (await db.KeyExistsAsync($"vehicle:{id}"))
            failures.Add($"{scenario}: vehicle:{id} deveria estar ausente");
        if (!await db.KeyExistsAsync($"deleted:{id}"))
            failures.Add($"{scenario}: deleted:{id} deveria existir");
    }

    private static void CheckNumber(
        List<string> failures, string scenario, Dictionary<string, string> hash, string field, double expected)
    {
        if (!hash.TryGetValue(field, out var raw) ||
            !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual))
        {
            failures.Add($"{scenario}: campo '{field}' ausente ou inválido (esperado {expected})");
            return;
        }

        if (Math.Abs(actual - expected) > Tolerance)
            failures.Add($"{scenario}: {field} esperado {expected}, atual {actual}");
    }
}
