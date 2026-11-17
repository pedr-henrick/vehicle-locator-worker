using StackExchange.Redis;
using VehicleLocator.Application.Replay;

namespace VehicleLocator.MockWriters;

public static class ReplayVerifyCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var timeout = TimeSpan.FromSeconds(args.Length > 0 ? int.Parse(args[0]) : 60);
        var sourceDb = args.Length > 1 ? int.Parse(args[1]) : 0;
        var replayDb = args.Length > 2 ? int.Parse(args[2]) : 1;
        var connection = Environment.GetEnvironmentVariable("VALKEY") ?? "localhost:6379";

        await using var mux = await ConnectionMultiplexer.ConnectAsync(connection);

        var deadline = DateTime.UtcNow + timeout;
        var consecutiveIdentical = 0;

        while (true)
        {
            var source = await ViewReader.ReadAsync(mux, sourceDb);
            var replay = await ViewReader.ReadAsync(mux, replayDb);

            if (source.Count == 0)
            {
                Console.WriteLine($"FALHA: db {sourceDb} (origem) está vazio; não há o que comparar.");
                return 2;
            }

            var diff = ViewComparer.Compare(source, replay);
            consecutiveIdentical = diff.IsIdentical ? consecutiveIdentical + 1 : 0;

            if (consecutiveIdentical >= 2)
            {
                Console.WriteLine($"OK: db {sourceDb} e db {replayDb} idênticos ({source.Count} chaves).");
                return 0;
            }

            if (DateTime.UtcNow >= deadline)
            {
                Console.WriteLine(
                    $"FALHA: replay não convergiu em {timeout.TotalSeconds:0}s " +
                    $"(origem={source.Count} chaves, replay={replay.Count}).");
                Print("só na origem", diff.OnlyInSource);
                Print("só no replay", diff.OnlyInReplay);
                Print("conteúdo diferente", diff.Different);
                return 1;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static void Print(string title, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return;

        Console.WriteLine($"  {title} ({keys.Count}):");
        foreach (var key in keys.Take(10))
            Console.WriteLine($"    {key}");
    }
}
