namespace VehicleLocator.MockWriters;

public enum StreamOpKind { InsertVehicle, UpdateVehicle, InsertPosition }

public sealed record StreamOp(
    StreamOpKind Kind,
    string VehicleId,
    string? State,
    double Lat,
    double Lng,
    DateTimeOffset Timestamp);

// Gera, de forma determinística (seed), a sequência de operações do cenário "stream".
// O valor final esperado não depende do sorteio: é sempre o do maior timestamp.
public static class StreamPlan
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] States = ["REGISTERED", "ACTIVE", "IDLE", "MAINTENANCE"];

    public static string StateAt(int k) => States[k % States.Length];

    public static double LatAt(int i) => Math.Round(-8.0 - i * 0.001, 3);

    public static double LngAt(int i) => Math.Round(-34.0 - i * 0.001, 3);

    public static string VehicleId(string prefix, int n) => $"{prefix}-stream-{n}";

    public static IReadOnlyList<StreamOp> Build(string prefix, StreamOptions options)
    {
        var rng = new Random(options.Seed);
        var inserts = new List<StreamOp>();
        var perVehicle = new List<List<StreamOp>>();

        for (var n = 1; n <= options.Vehicles; n++)
        {
            var id = VehicleId(prefix, n);
            inserts.Add(new StreamOp(StreamOpKind.InsertVehicle, id, StateAt(0), 0, 0, T0));

            var updates = new List<StreamOp>();
            for (var k = 1; k < options.Transitions; k++)
                updates.Add(new StreamOp(StreamOpKind.UpdateVehicle, id, StateAt(k), 0, 0, T0.AddMinutes(k)));

            var positions = new List<StreamOp>();
            for (var i = 0; i < options.Positions; i++)
                positions.Add(new StreamOp(StreamOpKind.InsertPosition, id, null, LatAt(i), LngAt(i), T0.AddSeconds(i)));

            perVehicle.Add(Interleave(Disturb(updates, rng, options), Disturb(positions, rng, options)));
        }

        // Os inserts de veículo vêm primeiro; depois, round-robin entre os veículos.
        var plan = new List<StreamOp>(inserts);
        var longest = perVehicle.Max(list => list.Count);
        for (var idx = 0; idx < longest; idx++)
            foreach (var list in perVehicle)
                if (idx < list.Count)
                    plan.Add(list[idx]);

        return plan;
    }

    // Troca vizinhos (fora de ordem) e repete operações (duplicatas).
    private static List<StreamOp> Disturb(List<StreamOp> ops, Random rng, StreamOptions options)
    {
        var shuffled = new List<StreamOp>(ops);
        for (var i = 0; i < shuffled.Count - 1; i++)
        {
            if (rng.Next(100) < options.OutOfOrderPercent)
            {
                (shuffled[i], shuffled[i + 1]) = (shuffled[i + 1], shuffled[i]);
                i++;
            }
        }

        var result = new List<StreamOp>(shuffled.Count);
        foreach (var op in shuffled)
        {
            result.Add(op);
            if (rng.Next(100) < options.DuplicatePercent)
                result.Add(op);
        }

        return result;
    }

    private static List<StreamOp> Interleave(List<StreamOp> a, List<StreamOp> b)
    {
        var result = new List<StreamOp>(a.Count + b.Count);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            if (i < a.Count) result.Add(a[i]);
            if (i < b.Count) result.Add(b[i]);
        }

        return result;
    }
}
