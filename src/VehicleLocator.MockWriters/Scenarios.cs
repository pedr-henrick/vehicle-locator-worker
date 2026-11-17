namespace VehicleLocator.MockWriters;

public static class Scenarios
{
    public const int VolumeCount = 200;

    // Grupo A: veículos + volume. Grupo B: posições.
    public static readonly string[] GroupA = ["lifecycle", "out-of-order", "delete", "volume"];

    public static readonly string[] GroupB =
    [
        "positions", "position-out-of-order", "position-duplicate",
        "position-before-vehicle", "position-after-delete"
    ];

    public static string[] All => [.. GroupA, .. GroupB];

    public static string[]? ForGroup(string group) => group switch
    {
        "a" => GroupA,
        "b" => GroupB,
        "all" => All,
        _ => null
    };

    public static string IdFor(string prefix, string scenario) => $"{prefix}-{scenario}-1";

    public static double VolumeLat(int i) => Math.Round(-9.0 - i * 0.001, 3);

    public static double VolumeLng(int i) => Math.Round(-35.0 - i * 0.001, 3);
}
