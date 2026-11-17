using Confluent.Kafka;

namespace VehicleLocator.Worker.Support.Kafka;

public interface IKafkaConsumerFactory
{
    IConsumer<string, string> Create(string groupId);
}
