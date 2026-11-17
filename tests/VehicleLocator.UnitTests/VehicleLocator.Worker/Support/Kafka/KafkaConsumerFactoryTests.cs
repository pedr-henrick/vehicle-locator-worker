using Confluent.Kafka;
using VehicleLocator.Worker.Support.Kafka;

namespace VehicleLocator.UnitTests.VehicleLocator.Worker.Support.Kafka;

public class KafkaConsumerFactoryTests
{
    [Fact]
    public void Offsets_are_committed_manually_never_automatically()
    {
        var config = KafkaConsumerFactory.BuildConfig(new KafkaOptions(), "g");

        Assert.False(config.EnableAutoCommit);
    }

    [Fact]
    public void New_groups_start_from_the_beginning_of_the_topic()
    {
        var config = KafkaConsumerFactory.BuildConfig(new KafkaOptions(), "g");

        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
    }

    [Fact]
    public void Group_and_bootstrap_servers_come_from_the_arguments_and_options()
    {
        var config = KafkaConsumerFactory.BuildConfig(
            new KafkaOptions { BootstrapServers = "broker:9092" }, "my-group");

        Assert.Equal("my-group", config.GroupId);
        Assert.Equal("broker:9092", config.BootstrapServers);
    }

    [Fact]
    public void Uses_cooperative_sticky_assignment_to_limit_partition_movement()
    {
        var config = KafkaConsumerFactory.BuildConfig(new KafkaOptions(), "g");

        Assert.Equal(PartitionAssignmentStrategy.CooperativeSticky, config.PartitionAssignmentStrategy);
    }

    [Fact]
    public void Defaults_point_to_the_vehicles_topic_and_the_external_listener()
    {
        var options = new KafkaOptions();

        Assert.Equal("fleet.public.vehicles", options.VehiclesTopic);
        Assert.Equal("localhost:9094", options.BootstrapServers);
    }
}
