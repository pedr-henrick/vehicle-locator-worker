using Confluent.Kafka;
using Microsoft.Extensions.Options;
using VehicleLocator.Application.Shared;

namespace VehicleLocator.Worker.Kafka
{
    public sealed class ConsumerLoop(IOptions<KafkaOptions> options, ILogger<ConsumerLoop> logger)
    {
        public void Run(
            string topic,
            string groupId,
            Func<string?, string?, CancellationToken, Task<ProjectionResult>> handle,
            CancellationToken ct)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = options.Value.BootstrapServers,
                GroupId = groupId,
                EnableAutoCommit = false,                 // commit manual, só após gravar no Valkey
                AutoOffsetReset = AutoOffsetReset.Earliest,
                PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
            };

            using var consumer = new ConsumerBuilder<string, string>(config).Build();
            consumer.Subscribe(topic);
            logger.LogInformation("Consumer started topic={Topic} group={Group}", topic, groupId);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = consumer.Consume(ct);
                    if (result is null || result.IsPartitionEOF) continue;

                    try
                    {
                        var outcome = handle(result.Message.Key, result.Message.Value, ct)
                            .GetAwaiter().GetResult();

                        if (outcome == ProjectionResult.Invalid)
                            logger.LogWarning("Invalid event skipped at {Tpo}", result.TopicPartitionOffset);

                        consumer.Commit(result);          // só depois de gravar
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed at {Tpo}, will retry", result.TopicPartitionOffset);
                        consumer.Seek(result.TopicPartitionOffset); // não commita, relê o mesmo offset
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                consumer.Close();
            }
        }
    }
}
