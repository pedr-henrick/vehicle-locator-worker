using Npgsql;

namespace VehicleLocator.MockWriters;

public class ScenarioWriter(NpgsqlDataSource ds)
{
    // Base fixa: o mesmo cenário gera sempre os mesmos timestamps.
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int ProgressEvery = 50;

    public async Task RunAsync(string scenario, string id)
    {
        switch (scenario)
        {
            case "lifecycle":
                // Esperado: state=MAINTENANCE
                await InsertVehicle(id, "REGISTERED", T0);
                await UpdateVehicle(id, "ACTIVE", T0.AddMinutes(1));
                await UpdateVehicle(id, "IDLE", T0.AddMinutes(2));
                await UpdateVehicle(id, "MAINTENANCE", T0.AddMinutes(3));
                break;

            case "out-of-order":
                // Último update com updated_at antigo e LSN maior. Esperado: state=ACTIVE.
                await InsertVehicle(id, "REGISTERED", T0);
                await UpdateVehicle(id, "ACTIVE", T0.AddMinutes(10));
                await UpdateVehicle(id, "IDLE", T0.AddMinutes(5));
                break;

            case "delete":
                // Esperado: vehicle:{id} ausente e deleted:{id} presente.
                await InsertVehicle(id, "REGISTERED", T0);
                await UpdateVehicle(id, "ACTIVE", T0.AddMinutes(1));
                await DeleteVehicle(id);
                break;

            case "volume":
                // 200 posições em ordem embaralhada (37 é coprimo de 200, logo é uma
                // permutação) e uma duplicata a cada 10. Esperado: a posição i = 199.
                await InsertVehicle(id, "REGISTERED", T0);
                for (var k = 0; k < Scenarios.VolumeCount; k++)
                {
                    var i = k * 37 % Scenarios.VolumeCount;
                    await InsertPosition(id, Scenarios.VolumeLat(i), Scenarios.VolumeLng(i), T0.AddMinutes(i));
                    if (k % 10 == 0)
                        await InsertPosition(id, Scenarios.VolumeLat(i), Scenarios.VolumeLng(i), T0.AddMinutes(i));
                }
                break;

            case "positions":
                // Esperado: lat=-9.62, lng=-35.72, state=REGISTERED.
                await InsertVehicle(id, "REGISTERED", T0);
                await InsertPosition(id, -9.60, -35.70, T0.AddMinutes(1));
                await InsertPosition(id, -9.61, -35.71, T0.AddMinutes(2));
                await InsertPosition(id, -9.62, -35.72, T0.AddMinutes(3));
                break;

            case "position-out-of-order":
                // Esperado: lat=-9.70, lng=-35.70 (a de T0+10min).
                await InsertVehicle(id, "REGISTERED", T0);
                await InsertPosition(id, -9.70, -35.70, T0.AddMinutes(10));
                await InsertPosition(id, -9.50, -35.50, T0.AddMinutes(5));
                break;

            case "position-duplicate":
                // Esperado: lat=-9.60, lng=-35.70.
                await InsertVehicle(id, "REGISTERED", T0);
                await InsertPosition(id, -9.60, -35.70, T0.AddMinutes(1));
                await InsertPosition(id, -9.60, -35.70, T0.AddMinutes(1));
                break;

            case "position-before-vehicle":
                // Esperado: lat/lng e, depois, state=REGISTERED (sem estado presumido).
                await InsertPosition(id, -9.60, -35.70, T0.AddMinutes(1));
                await Task.Delay(1500);
                await InsertVehicle(id, "REGISTERED", T0);
                break;

            case "position-after-delete":
                // Esperado: vehicle:{id} ausente, deleted:{id} presente.
                await InsertVehicle(id, "REGISTERED", T0);
                await DeleteVehicle(id);
                await Task.Delay(1500);
                await InsertPosition(id, -9.60, -35.70, T0.AddMinutes(1));
                break;

            // ---- snapshot (Debezium op=r) ----
            case "snapshot-only":
                // Executado antes do connector; nunca mais tocado.
                // Esperado: state=REGISTERED, lat=-9.30, lng=-35.30 (tudo vindo do snapshot).
                await InsertVehicle(id, "REGISTERED", T0);
                await InsertPosition(id, -9.30, -35.30, T0.AddMinutes(1));
                break;

            case "snapshot-then-stream-pre":
                // Antes do connector (snapshot).
                await InsertVehicle(id, "REGISTERED", T0);
                await InsertPosition(id, -9.40, -35.40, T0.AddMinutes(1));
                break;

            case "snapshot-then-stream-post":
                // Depois do snapshot (streaming), sobre as mesmas linhas.
                // Esperado: state=ACTIVE, lat=-9.41, lng=-35.41.
                await UpdateVehicle(id, "ACTIVE", T0.AddMinutes(5));
                await InsertPosition(id, -9.41, -35.41, T0.AddMinutes(6));
                break;

            default:
                throw new ArgumentException($"Cenário desconhecido: {scenario}", nameof(scenario));
        }
    }

    // Executa a sequência determinística de StreamPlan, limitada por RatePerSecond (0 = sem limite).
    // Imprime "progresso: n/total" a cada 50 operações (o e2e usa isso para saber que o stream começou).
    public async Task RunStreamAsync(string prefix, StreamOptions options, Action<string> log)
    {
        var plan = StreamPlan.Build(prefix, options);
        var delay = options.RatePerSecond > 0
            ? TimeSpan.FromMilliseconds(1000.0 / options.RatePerSecond)
            : TimeSpan.Zero;

        for (var n = 0; n < plan.Count; n++)
        {
            var op = plan[n];
            switch (op.Kind)
            {
                case StreamOpKind.InsertVehicle:
                    await InsertVehicle(op.VehicleId, op.State!, op.Timestamp);
                    break;
                case StreamOpKind.UpdateVehicle:
                    await UpdateVehicle(op.VehicleId, op.State!, op.Timestamp);
                    break;
                case StreamOpKind.InsertPosition:
                    await InsertPosition(op.VehicleId, op.Lat, op.Lng, op.Timestamp);
                    break;
            }

            if ((n + 1) % ProgressEvery == 0)
                log($"progresso: {n + 1}/{plan.Count}");

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
        }

        log($"concluído: {plan.Count} operações");
    }

    private Task InsertVehicle(string id, string state, DateTimeOffset ts) =>
        Exec("INSERT INTO vehicles (id, state, updated_at) VALUES ($1, $2, $3)", id, state, ts);

    private Task UpdateVehicle(string id, string state, DateTimeOffset ts) =>
        Exec("UPDATE vehicles SET state = $2, updated_at = $3 WHERE id = $1", id, state, ts);

    private Task DeleteVehicle(string id) =>
        Exec("DELETE FROM vehicles WHERE id = $1", id);

    private Task InsertPosition(string id, double lat, double lng, DateTimeOffset recordedAt) =>
        Exec("INSERT INTO positions (vehicle_id, lat, lng, recorded_at) VALUES ($1, $2, $3, $4)",
            id, lat, lng, recordedAt);

    private async Task Exec(string sql, params object[] values)
    {
        await using var cmd = ds.CreateCommand(sql);
        foreach (var v in values)
            cmd.Parameters.Add(new NpgsqlParameter { Value = v });
        await cmd.ExecuteNonQueryAsync();
    }
}