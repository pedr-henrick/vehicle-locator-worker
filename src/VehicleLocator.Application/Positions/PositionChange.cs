namespace VehicleLocator.Application.Positions;

public sealed record PositionChange(string VehicleId, double Lat, double Lng, long RecordedAtMicros, long Lsn);
