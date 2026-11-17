using VehicleLocator.Worker.Support.Kafka;
using VehicleLocator.Worker.Support.Valkey;

namespace VehicleLocator.Worker.Support.Replay;

public static class ReplayConfiguration
{
    public static void ApplyGroupSuffix(KafkaOptions kafka, ReplayOptions replay, DateTimeOffset now)
    {
        if (!replay.Enabled)
            return;

        var suffix = string.IsNullOrWhiteSpace(replay.GroupSuffix)
            ? now.UtcDateTime.ToString("yyyyMMddHHmmss")
            : replay.GroupSuffix;

        kafka.VehiclesGroupId = $"{kafka.VehiclesGroupId}-replay-{suffix}";
        kafka.PositionsGroupId = $"{kafka.PositionsGroupId}-replay-{suffix}";
    }

    public static void Validate(ReplayOptions replay, ValkeyOptions valkey)
    {
        if (replay.Enabled && valkey.Database == 0)
            throw new InvalidOperationException(
                "Replay:Enabled exige Valkey:Database diferente de 0, para não sobrescrever o cache real.");
    }
}
