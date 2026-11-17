namespace VehicleLocator.Application.Vehicles;

public enum VehicleChangeKind { Upsert, Delete }

public sealed record VehicleChange(string VehicleId, VehicleChangeKind Kind, string? State, long UpdatedAtMicros, long Lsn);
