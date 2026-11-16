using Confluent.Kafka;
using Microsoft.Extensions.Options;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Worker.Support.Kafka;

/// <summary>
/// Processes a message and produces a projection result.
/// </summary>
/// <param name="key">The message key.</param>
/// <param name="value">The message value.</param>
/// <param name="ct">A token to monitor for cancellation requests.</param>
/// <returns>A task representing the asynchronous operation, with the projection result.</returns>
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

    /// <summary>
    /// Processes a consumed message, committing it after handling or seeking it for retry if handling fails.
    /// </summary>
    /// <remarks>Invalid events are logged and committed. Exceptions other than <see
    /// cref="OperationCanceledException"/> are logged, and the message is sought for retry; an optional delay is
    /// applied before returning.</remarks>
    /// <param name="consumer">The Kafka consumer used to commit or seek the message offset.</param>
    /// <param name="result">The consumed message and its topic, partition, and offset.</param>
    /// <param name="handle">The handler invoked with the message key, value, and cancellation token.</param>
    /// <param name="ct">The cancellation token passed to the handler.</param>
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
