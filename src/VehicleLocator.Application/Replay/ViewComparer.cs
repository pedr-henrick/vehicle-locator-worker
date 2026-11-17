namespace VehicleLocator.Application.Replay;

public static class ViewComparer
{
    public static ViewDiff Compare(ViewSnapshot source, ViewSnapshot replay)
    {
        var onlyInSource = source.Entries.Keys
            .Where(k => !replay.Entries.ContainsKey(k))
            .Order(StringComparer.Ordinal)
            .ToList();

        var onlyInReplay = replay.Entries.Keys
            .Where(k => !source.Entries.ContainsKey(k))
            .Order(StringComparer.Ordinal)
            .ToList();

        var different = source.Entries
            .Where(kv => replay.Entries.TryGetValue(kv.Key, out var other) && !SameFields(kv.Value, other))
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new ViewDiff(onlyInSource, onlyInReplay, different);
    }

    // A ordem dos campos do hash não importa.
    private static bool SameFields(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count &&
        a.All(kv => b.TryGetValue(kv.Key, out var v) && string.Equals(kv.Value, v, StringComparison.Ordinal));
}
