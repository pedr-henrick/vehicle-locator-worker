namespace VehicleLocator.Application.Replay;

public sealed record ViewDiff(
    IReadOnlyList<string> OnlyInSource,
    IReadOnlyList<string> OnlyInReplay,
    IReadOnlyList<string> Different)
{
    public bool IsIdentical => OnlyInSource.Count == 0 && OnlyInReplay.Count == 0 && Different.Count == 0;
}