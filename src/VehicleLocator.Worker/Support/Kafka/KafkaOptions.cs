namespace VehicleLocator.Worker.Support.Kafka;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9094";
    public string VehiclesTopic { get; set; } = "fleet.public.vehicles";
    public string VehiclesGroupId { get; set; } = "vehicle-cache-vehicles";
    public int RetryDelayMs { get; set; } = 1000;
}
