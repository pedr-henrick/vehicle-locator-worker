namespace VehicleLocator.Worker.Support.Kafka;

public interface IConsumerLoop
{
    void Run(string topic, string groupId, MessageHandler handle, CancellationToken ct);
}
