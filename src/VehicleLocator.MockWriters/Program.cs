using Npgsql;
using VehicleLocator.MockWriters;

const string usage =
    "Uso:\n" +
    "  <cenario> [prefixo]            (id = <prefixo>-1)\n" +
    "  seed <prefixo> [a|b|snapshot-pre|snapshot-post|all]   (id = <prefixo>-<cenario>-1)\n" +
    "  stream <prefixo> [vehicles=3] [positions=60] [transitions=8] [rate=0] [seed=42] [ooo=15] [dup=10]\n" +
    "  assert-view <prefixo> [timeoutSeg=120] [db=0]\n" +
    "  verify-replay [timeoutSeg=60] [dbOrigem=0] [dbReplay=1]\n" +
    "Cenários: lifecycle | out-of-order | delete | volume | positions | position-out-of-order | " +
    "position-duplicate | position-before-vehicle | position-after-delete | " +
    "snapshot-only | snapshot-then-stream-pre | snapshot-then-stream-post";

var command = args.Length > 0 ? args[0] : "lifecycle";

if (command == "verify-replay")
    return await ReplayVerifyCommand.RunAsync(args[1..]);

if (command == "assert-view")
    return await AssertViewCommand.RunAsync(args[1..]);

var connectionString = Environment.GetEnvironmentVariable("POSTGRES")
    ?? "Host=localhost;Port=5432;Username=fleet;Password=fleet;Database=fleet";

await using var ds = NpgsqlDataSource.Create(connectionString);
var writer = new ScenarioWriter(ds);

if (command == "stream")
{
    var streamPrefix = args.Length > 1 ? args[1] : "e2e";
    StreamOptions streamOptions;
    try
    {
        streamOptions = StreamOptions.Parse(args.Skip(2));
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine(ex.Message);
        Console.WriteLine(usage);
        return 1;
    }

    Console.WriteLine($"stream: prefixo '{streamPrefix}', {streamOptions}");
    await writer.RunStreamAsync(streamPrefix, streamOptions, Console.WriteLine);
    return 0;
}

if (command == "seed")
{
    var seedPrefix = args.Length > 1 ? args[1] : "e2e";
    var names = Scenarios.ForGroup(args.Length > 2 ? args[2] : "all");
    if (names is null)
    {
        Console.WriteLine(usage);
        return 1;
    }

    foreach (var name in names)
    {
        await writer.RunAsync(name, Scenarios.IdFor(seedPrefix, name));
        Console.WriteLine($"Cenário '{name}' executado ({Scenarios.IdFor(seedPrefix, name)}).");
    }

    return 0;
}

if (!Scenarios.All.Contains(command))
{
    Console.WriteLine(usage);
    return 1;
}

var prefix = args.Length > 1 ? args[1] : $"mw{DateTime.UtcNow:HHmmss}";
var id = $"{prefix}-1";
await writer.RunAsync(command, id);
Console.WriteLine($"Cenário '{command}' executado para o veículo '{id}'.");
return 0;
