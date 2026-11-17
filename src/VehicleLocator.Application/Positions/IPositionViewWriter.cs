namespace VehicleLocator.Application.Positions;

public interface IPositionViewWriter
{
    Task<PositionDecision> ApplyAsync(PositionChange change);
}