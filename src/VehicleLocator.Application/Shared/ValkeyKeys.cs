namespace VehicleLocator.Application.Shared;

public static class ValkeyKeys
{
    public static string Vehicle(string id) => $"vehicle:{id}";
    public static string Deleted(string id) => $"deleted:{id}";

    public const string State = "state";
    public const string StateTs = "stateTs";
    public const string StateLsn = "stateLsn";

    public const string Lat = "lat";
    public const string Lng = "lng";
    public const string PosTs = "posTs";
    public const string PosLsn = "posLsn";
}
