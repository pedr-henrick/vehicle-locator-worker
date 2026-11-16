namespace VehicleLocator.Application.Vehicles
{
    public enum VehicleChangeKind { Upsert, Delete }

    /// <summary>Evento interno de veículo, já traduzido do envelope Debezium.</summary>
    /// <param name="UpdatedAtMicros">Microssegundos desde a época Unix (sem perder precisão).</param>
    /// <param name="Lsn">source.lsn do Debezium, usado como desempate.</param>
    public sealed record VehicleChange(
        string VehicleId,
        VehicleChangeKind Kind,
        string? State,
        long UpdatedAtMicros,
        long Lsn);
}
