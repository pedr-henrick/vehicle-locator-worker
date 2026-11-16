namespace VehicleLocator.Worker.Kafka
{
    public sealed class KafkaOptions
    {
        public string BootstrapServers { get; set; } = "localhost:9094";
        public string VehiclesTopic { get; set; } = "fleet.public.vehicles";
        public string VehiclesGroupId { get; set; } = "vehicle-cache-vehicles";
    }

    public sealed class ValkeyOptions
    {
        public string ConnectionString { get; set; } = "localhost:6379";
    }
}
