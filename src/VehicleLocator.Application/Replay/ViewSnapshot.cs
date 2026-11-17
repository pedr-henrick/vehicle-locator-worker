namespace VehicleLocator.Application.Replay;

public sealed record ViewSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Entries)
{
    public int Count => Entries.Count;
}