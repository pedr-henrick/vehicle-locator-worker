namespace VehicleLocator.Worker.Support.Kafka;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9094";

    public string VehiclesTopic { get; set; } = "fleet.public.vehicles";
    public string VehiclesGroupId { get; set; } = "vehicle-cache-vehicles";

    public string PositionsTopic { get; set; } = "fleet.public.positions";
    public string PositionsGroupId { get; set; } = "vehicle-cache-positions";

    public int RetryDelayMs { get; set; } = 1000;
}
