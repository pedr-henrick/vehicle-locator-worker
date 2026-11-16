namespace VehicleLocator.Worker.Support.Kafka;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; }
    public string VehiclesTopic { get; set; }
    public string VehiclesGroupId { get; set; }
}
