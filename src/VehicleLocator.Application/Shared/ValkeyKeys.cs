namespace VehicleLocator.Application.Shared
{
    public static class ValkeyKeys
    {
        public static string Vehicle(string id) => $"vehicle:{id}";
        public static string Deleted(string id) => $"deleted:{id}";

        // Campos do hash vehicle:{id}. Veículos escreve estes três; posições escreverá lat/lng/pos*.
        public const string State = "state";
        public const string StateTs = "stateTs";
        public const string StateLsn = "stateLsn";
    }
}
