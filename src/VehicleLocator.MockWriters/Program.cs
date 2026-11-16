using Npgsql;

var scenario = args.Length > 0 ? args[0] : "lifecycle";
var prefix = args.Length > 1 ? args[1] : $"mw{DateTime.UtcNow:HHmmss}";
var connectionString = Environment.GetEnvironmentVariable("POSTGRES")
    ?? "Host=localhost;Port=5432;Username=fleet;Password=fleet;Database=fleet";

await using var ds = NpgsqlDataSource.Create(connectionString);

var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var id = $"{prefix}-1";

switch (scenario)
{
    case "lifecycle":
        await Insert(id, "REGISTERED", t0);
        await Update(id, "ACTIVE", t0.AddMinutes(1));
        await Update(id, "IDLE", t0.AddMinutes(2));
        await Update(id, "MAINTENANCE", t0.AddMinutes(3));
        break;

    case "out-of-order":
        await Insert(id, "REGISTERED", t0);
        await Update(id, "ACTIVE", t0.AddMinutes(10));
        await Update(id, "IDLE", t0.AddMinutes(5));
        break;

    case "delete":
        await Insert(id, "REGISTERED", t0);
        await Update(id, "ACTIVE", t0.AddMinutes(1));
        await Delete(id);
        break;

    default:
        Console.WriteLine("Cenários: lifecycle | out-of-order | delete");
        return 1;
}

Console.WriteLine($"Cenário '{scenario}' executado para o veículo '{id}'.");
return 0;

async Task Insert(string vehicleId, string state, DateTimeOffset ts) =>
    await Exec("INSERT INTO vehicles (id, state, updated_at) VALUES ($1, $2, $3)", vehicleId, state, ts);

async Task Update(string vehicleId, string state, DateTimeOffset ts) =>
    await Exec("UPDATE vehicles SET state = $2, updated_at = $3 WHERE id = $1", vehicleId, state, ts);

async Task Delete(string vehicleId) =>
    await Exec("DELETE FROM vehicles WHERE id = $1", vehicleId);

async Task Exec(string sql, params object[] values)
{
    await using var cmd = ds.CreateCommand(sql);
    foreach (var v in values)
        cmd.Parameters.Add(new NpgsqlParameter { Value = v });

    await cmd.ExecuteNonQueryAsync();
}