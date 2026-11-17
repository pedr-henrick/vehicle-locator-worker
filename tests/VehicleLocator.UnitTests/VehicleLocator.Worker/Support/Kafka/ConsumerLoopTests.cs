using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using VehicleLocator.Application.Shared;
using VehicleLocator.Worker.Support.Kafka;
using Xunit.Runner.Common;

namespace VehicleLocator.UnitTests.VehicleLocator.Worker.Support.Kafka;

public class ConsumerLoopTests
{
    private readonly IConsumer<string, string> _consumer = Substitute.For<IConsumer<string, string>>();
    private readonly IKafkaConsumerFactory _factory = Substitute.For<IKafkaConsumerFactory>();
    private readonly ConsumerLoop _loop;
    private readonly List<string> _events = [];

    public ConsumerLoopTests()
    {
        _factory.Create(Arg.Any<string>()).Returns(_consumer);

        _consumer
            .When(c => c.Commit(Arg.Any<ConsumeResult<string, string>>()))
            .Do(ci => _events.Add($"commit:{ci.Arg<ConsumeResult<string, string>>().Offset.Value}"));

        _loop = new ConsumerLoop(
            _factory,
            Options.Create(new KafkaOptions { RetryDelayMs = 0 }),
            NullLogger<ConsumerLoop>.Instance);
    }

    private static ConsumeResult<string, string> Msg(long offset, string? value = "v", string key = "k") => new()
    {
        Topic = "topic",
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, string> { Key = key, Value = value! }
    };

    private void RunWith(IReadOnlyList<ConsumeResult<string, string>> messages, MessageHandler handle)
    {
        using var cts = new CancellationTokenSource();
        var next = 0;

        _consumer.Consume(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (next < messages.Count) return messages[next++];
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        _loop.Run("topic", "group", handle, cts.Token);
    }

    [Fact]
    public void Creates_consumer_for_group_subscribes_to_topic_and_closes_on_shutdown()
    {
        RunWith([], (_, _, _) => Task.FromResult(ProjectionResult.Applied));

        _factory.Received(1).Create("group");
        _consumer.Received(1).Subscribe("topic");
        _consumer.Received(1).Close();
    }

    [Fact]
    public void Commits_only_after_the_handler_finished()
    {
        RunWith([Msg(5)], (_, _, _) =>
        {
            _events.Add("handle:5");
            return Task.FromResult(ProjectionResult.Applied);
        });

        Assert.Equal(["handle:5", "commit:5"], _events);
    }

    [Fact]
    public void Passes_key_and_value_to_the_handler()
    {
        string? gotKey = null, gotValue = null;

        RunWith([Msg(1, "payload", "vehicle-1")], (k, v, _) =>
        {
            gotKey = k;
            gotValue = v;
            return Task.FromResult(ProjectionResult.Applied);
        });

        Assert.Equal("vehicle-1", gotKey);
        Assert.Equal("payload", gotValue);
    }

    [Fact]
    public void Tombstone_reaches_the_handler_with_null_value_and_is_committed()
    {
        var values = new List<string?>();

        RunWith([Msg(7, value: null)], (_, v, _) =>
        {
            values.Add(v);
            return Task.FromResult(ProjectionResult.IgnoredTombstone);
        });

        Assert.Equal([(string?)null], values);
        Assert.Contains("commit:7", _events);
    }

    [Fact]
    public void Messages_are_processed_and_committed_in_order()
    {
        RunWith([Msg(1), Msg(2), Msg(3)], (_, _, _) => Task.FromResult(ProjectionResult.Applied));

        Assert.Equal(["commit:1", "commit:2", "commit:3"], _events);
    }

    [Fact]
    public void Failing_handler_does_not_commit_and_seeks_back_to_the_same_offset()
    {
        RunWith([Msg(5)], (_, _, _) => throw new InvalidOperationException("boom"));

        _consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, string>>());
        _consumer.Received(1).Seek(Arg.Is<TopicPartitionOffset>(t =>
            t.Topic == "topic" && t.Partition.Value == 0 && t.Offset.Value == 5));
    }

    [Fact]
    public void Message_is_retried_until_the_handler_succeeds()
    {
        var calls = 0;
        var message = Msg(9);

        RunWith([message, message, message], (_, _, _) =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("transient");
            return Task.FromResult(ProjectionResult.Applied);
        });

        Assert.Equal(3, calls);
        _consumer.Received(2).Seek(Arg.Any<TopicPartitionOffset>());
        _consumer.Received(1).Commit(Arg.Any<ConsumeResult<string, string>>());
        Assert.Equal(["commit:9"], _events);
    }

    [Fact]
    public void Invalid_event_is_skipped_but_still_committed()
    {
        RunWith([Msg(3)], (_, _, _) => Task.FromResult(ProjectionResult.Invalid));

        Assert.Equal(["commit:3"], _events);
        _consumer.DidNotReceive().Seek(Arg.Any<TopicPartitionOffset>());
    }

    [Fact]
    public void Partition_eof_markers_are_ignored()
    {
        var eof = Msg(4);
        eof.IsPartitionEOF = true;
        var handled = 0;

        RunWith([eof], (_, _, _) =>
        {
            handled++;
            return Task.FromResult(ProjectionResult.Applied);
        });

        Assert.Equal(0, handled);
        _consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, string>>());
    }

    [Fact]
    public void Cancellation_inside_the_handler_stops_without_commit_or_seek()
    {
        RunWith([Msg(2)], (_, _, _) => throw new OperationCanceledException());

        _consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, string>>());
        _consumer.DidNotReceive().Seek(Arg.Any<TopicPartitionOffset>());
        _consumer.Received(1).Close();
    }
}
