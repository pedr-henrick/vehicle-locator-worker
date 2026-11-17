using Confluent.Kafka;
using Microsoft.Extensions.Options;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Worker.Support.Kafka;

public delegate Task<ProjectionResult> MessageHandler(string key, string value, CancellationToken ct);

public class ConsumerLoop(IKafkaConsumerFactory factory, IOptions<KafkaOptions> options, ILogger<ConsumerLoop> logger) : IConsumerLoop
{
    public void Run(string topic, string groupId, MessageHandler handle, CancellationToken ct)
    {
        using var consumer = factory.Create(groupId);
        consumer.Subscribe(topic);
        logger.LogInformation("Consumer started topic={Topic} group={Group}", topic, groupId);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = consumer.Consume(ct);
                if (result is null || result.IsPartitionEOF) continue;

                Process(consumer, result, handle, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close();
        }
    }

    public void Process(IConsumer<string, string> consumer, ConsumeResult<string, string> result, MessageHandler handle, CancellationToken ct)
    {
        try
        {
            var outcome = handle(result.Message.Key, result.Message.Value, ct)
                .GetAwaiter().GetResult();

            if (outcome == ProjectionResult.Invalid)
                logger.LogWarning("Invalid event skipped at {Tpo}", result.TopicPartitionOffset);

            consumer.Commit(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed at {Tpo}, will retry", result.TopicPartitionOffset);
            consumer.Seek(result.TopicPartitionOffset);

            if (options.Value.RetryDelayMs > 0)
                Thread.Sleep(options.Value.RetryDelayMs);
        }
    }
}
