using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace VehicleLocator.Worker.Support.Kafka;

public sealed class KafkaConsumerFactory(IOptions<KafkaOptions> options) : IKafkaConsumerFactory
{
    public static ConsumerConfig BuildConfig(KafkaOptions options, string groupId) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = groupId,
        EnableAutoCommit = false,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
    };

    public IConsumer<string, string> Create(string groupId) =>
        new ConsumerBuilder<string, string>(BuildConfig(options.Value, groupId)).Build();
}
