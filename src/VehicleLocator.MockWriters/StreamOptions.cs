using System.Globalization;

namespace VehicleLocator.MockWriters;

// Parâmetros do cenário "stream". O assert-view usa Default; o e2e só altera RatePerSecond.
public sealed record StreamOptions(
    int Vehicles = 3,
    int Positions = 60,
    int Transitions = 8,
    int RatePerSecond = 0,
    int Seed = 42,
    int OutOfOrderPercent = 15,
    int DuplicatePercent = 10)
{
    public static StreamOptions Default { get; } = new();

    // Aceita pares chave=valor: vehicles, positions, transitions, rate, seed, ooo, dup.
    public static StreamOptions Parse(IEnumerable<string> pairs)
    {
        var options = Default;
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException($"Argumento inválido '{pair}' (use chave=valor, com valor inteiro).");

            options = parts[0] switch
            {
                "vehicles" => options with { Vehicles = value },
                "positions" => options with { Positions = value },
                "transitions" => options with { Transitions = value },
                "rate" => options with { RatePerSecond = value },
                "seed" => options with { Seed = value },
                "ooo" => options with { OutOfOrderPercent = value },
                "dup" => options with { DuplicatePercent = value },
                _ => throw new ArgumentException($"Opção desconhecida '{parts[0]}'.")
            };
        }

        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Vehicles < 1) throw new ArgumentException("vehicles deve ser >= 1.");
        if (Positions < 1) throw new ArgumentException("positions deve ser >= 1.");
        if (Transitions < 1) throw new ArgumentException("transitions deve ser >= 1.");
        if (RatePerSecond < 0) throw new ArgumentException("rate deve ser >= 0 (0 = sem limite).");
        if (OutOfOrderPercent is < 0 or > 100) throw new ArgumentException("ooo deve estar entre 0 e 100.");
        if (DuplicatePercent is < 0 or > 100) throw new ArgumentException("dup deve estar entre 0 e 100.");
    }
}
