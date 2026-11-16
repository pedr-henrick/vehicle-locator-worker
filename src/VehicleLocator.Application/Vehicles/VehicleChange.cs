namespace VehicleLocator.Application.Vehicles
{
    public enum VehicleChangeKind { Upsert, Delete }

    /// <summary>
    /// Represents a change to a vehicle.
    /// </summary>
    /// <param name="VehicleId">The identifier of the vehicle.</param>
    /// <param name="Kind">The kind of change.</param>
    /// <param name="State">The vehicle's state, if available.</param>
    /// <param name="UpdatedAtMicros">The update time, in microseconds.</param>
    /// <param name="Lsn">The log sequence number associated with the change.</param>
    public sealed record VehicleChange(
        string VehicleId,
        VehicleChangeKind Kind,
        string? State,
        long UpdatedAtMicros,
        long Lsn);
}
