namespace VehicleLocator.Application.Vehicles;

public interface IVehicleViewWriter
{
    Task<VehicleDecision> ApplyAsync(VehicleChange change);
}
