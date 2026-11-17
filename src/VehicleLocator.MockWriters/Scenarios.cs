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

    // Executados ANTES de o connector existir: viram eventos op=r no snapshot do Debezium.
    public static readonly string[] SnapshotPre = ["snapshot-only", "snapshot-then-stream-pre"];

    // Executados DEPOIS do snapshot: streaming sobre linhas que vieram do snapshot.
    public static readonly string[] SnapshotPost = ["snapshot-then-stream-post"];

    public static string[] All => [.. SnapshotPre, .. GroupA, .. GroupB, .. SnapshotPost];

    // Cenários verificados pelo assert-view (nomes-base; pre/post compartilham o id).
    public static string[] Checked => [.. GroupA, .. GroupB, "snapshot-only", "snapshot-then-stream"];

    public static string[]? ForGroup(string group) => group switch
    {
        "a" => GroupA,
        "b" => GroupB,
        "snapshot-pre" => SnapshotPre,
        "snapshot-post" => SnapshotPost,
        "all" => All,
        _ => null
    };

    public static string BaseName(string scenario) =>
        scenario.EndsWith("-pre", StringComparison.Ordinal) ? scenario[..^4] :
        scenario.EndsWith("-post", StringComparison.Ordinal) ? scenario[..^5] :
        scenario;

    public static string IdFor(string prefix, string scenario) => $"{prefix}-{BaseName(scenario)}-1";

    public static double VolumeLat(int i) => Math.Round(-9.0 - i * 0.001, 3);

    public static double VolumeLng(int i) => Math.Round(-35.0 - i * 0.001, 3);
}