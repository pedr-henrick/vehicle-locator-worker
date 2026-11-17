using Npgsql;
using VehicleLocator.MockWriters;

if (args.Length > 0 && args[0] == "verify-replay")
    return await ReplayVerifyCommand.RunAsync(args[1..]);

var scenario = args.Length > 0 ? args[0] : "lifecycle";
var prefix = args.Length > 1 ? args[1] : $"mw{DateTime.UtcNow:HHmmss}";
var connectionString = Environment.GetEnvironmentVariable("POSTGRES")
    ?? "Host=localhost;Port=5432;Username=fleet;Password=fleet;Database=fleet";

await using var ds = NpgsqlDataSource.Create(connectionString);

// Base fixa: o mesmo cenário gera sempre os mesmos timestamps.
var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var id = $"{prefix}-1";

switch (scenario)
{
    // ---- veículos ----
    case "lifecycle":
        // Esperado: state=MAINTENANCE
        await InsertVehicle(id, "REGISTERED", t0);
        await UpdateVehicle(id, "ACTIVE", t0.AddMinutes(1));
        await UpdateVehicle(id, "IDLE", t0.AddMinutes(2));
        await UpdateVehicle(id, "MAINTENANCE", t0.AddMinutes(3));
        break;

    case "out-of-order":
        // O último update tem updated_at antigo, mas LSN maior.
        // Esperado: state=ACTIVE, e o log do worker mostra IgnoreStale.
        await InsertVehicle(id, "REGISTERED", t0);
        await UpdateVehicle(id, "ACTIVE", t0.AddMinutes(10));
        await UpdateVehicle(id, "IDLE", t0.AddMinutes(5));
        break;

    case "delete":
        // Esperado: vehicle:{id} ausente e deleted:{id} presente.
        await InsertVehicle(id, "REGISTERED", t0);
        await UpdateVehicle(id, "ACTIVE", t0.AddMinutes(1));
        await DeleteVehicle(id);
        break;

    // ---- posições ----
    case "positions":
        // Esperado: lat=-9.62, lng=-35.72, posTs = t0+3min, state=REGISTERED.
        await InsertVehicle(id, "REGISTERED", t0);
        await InsertPosition(id, -9.60, -35.70, t0.AddMinutes(1));
        await InsertPosition(id, -9.61, -35.71, t0.AddMinutes(2));
        await InsertPosition(id, -9.62, -35.72, t0.AddMinutes(3));
        break;

    case "position-out-of-order":
        // A segunda posição tem recorded_at antigo, mas LSN maior.
        // Esperado: lat=-9.70 (a de t0+10min), e o log mostra IgnoreStale.
        await InsertVehicle(id, "REGISTERED", t0);
        await InsertPosition(id, -9.70, -35.70, t0.AddMinutes(10));
        await InsertPosition(id, -9.50, -35.50, t0.AddMinutes(5));
        break;

    case "position-duplicate":
        // Duas linhas idênticas. Esperado: lat=-9.60, lng=-35.70, sem erro.
        await InsertVehicle(id, "REGISTERED", t0);
        await InsertPosition(id, -9.60, -35.70, t0.AddMinutes(1));
        await InsertPosition(id, -9.60, -35.70, t0.AddMinutes(1));
        break;

    case "position-before-vehicle":
        // A posição entra no banco antes do veículo.
        // Esperado: hash com lat/lng e, depois, state=REGISTERED (sem estado presumido).
        await InsertPosition(id, -9.60, -35.70, t0.AddMinutes(1));
        await Task.Delay(1500);
        await InsertVehicle(id, "REGISTERED", t0);
        break;

    case "position-after-delete":
        // Esperado: vehicle:{id} ausente, deleted:{id} presente, sem resíduo de posição.
        await InsertVehicle(id, "REGISTERED", t0);
        await DeleteVehicle(id);
        await Task.Delay(1500);
        await InsertPosition(id, -9.60, -35.70, t0.AddMinutes(1));
        break;

    default:
        Console.WriteLine(
            "Cenários: lifecycle | out-of-order | delete | positions | position-out-of-order | " +
            "position-duplicate | position-before-vehicle | position-after-delete | " +
            "verify-replay [timeout] [dbOrigem] [dbReplay]");
        return 1;
}

Console.WriteLine($"Cenário '{scenario}' executado para o veículo '{id}'.");
return 0;

async Task InsertVehicle(string vehicleId, string state, DateTimeOffset ts) =>
    await Exec("INSERT INTO vehicles (id, state, updated_at) VALUES ($1, $2, $3)", vehicleId, state, ts);

async Task UpdateVehicle(string vehicleId, string state, DateTimeOffset ts) =>
    await Exec("UPDATE vehicles SET state = $2, updated_at = $3 WHERE id = $1", vehicleId, state, ts);

async Task DeleteVehicle(string vehicleId) =>
    await Exec("DELETE FROM vehicles WHERE id = $1", vehicleId);

async Task InsertPosition(string vehicleId, double lat, double lng, DateTimeOffset recordedAt) =>
    await Exec(
        "INSERT INTO positions (vehicle_id, lat, lng, recorded_at) VALUES ($1, $2, $3, $4)",
        vehicleId, lat, lng, recordedAt);

async Task Exec(string sql, params object[] values)
{
    await using var cmd = ds.CreateCommand(sql);
    foreach (var v in values)
        cmd.Parameters.Add(new NpgsqlParameter { Value = v });
    await cmd.ExecuteNonQueryAsync();
}