namespace VehicleLocator.Application.Shared;

public enum ProjectionResult
{
    Applied,
    IgnoredStale,
    IgnoredDuplicate,
    IgnoredDeleted,
    IgnoredTombstone,
    Invalid
}
