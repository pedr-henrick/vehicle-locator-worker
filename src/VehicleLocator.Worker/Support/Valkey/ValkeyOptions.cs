namespace VehicleLocator.Worker.Support.Valkey;

public sealed class ValkeyOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    // Banco lógico do Valkey. 0 = cache real; replay usa outro (ex.: 1).
    public int Database { get; set; } = 0;
}