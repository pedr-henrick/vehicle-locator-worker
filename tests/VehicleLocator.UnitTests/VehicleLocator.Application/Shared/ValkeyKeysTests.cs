using VehicleLocator.Application.Shared;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Shared;

public class ValkeyKeysTests
{
    [Fact]
    public void Key_formats_are_stable()
    {
        Assert.Equal("vehicle:v-1", ValkeyKeys.Vehicle("v-1"));
        Assert.Equal("deleted:v-1", ValkeyKeys.Deleted("v-1"));
    }

    [Fact]
    public void Vehicle_and_deleted_keys_never_collide()
    {
        Assert.NotEqual(ValkeyKeys.Vehicle("x"), ValkeyKeys.Deleted("x"));
    }

    [Fact]
    public void Field_names_are_stable()
    {
        Assert.Equal("state", ValkeyKeys.State);
        Assert.Equal("stateTs", ValkeyKeys.StateTs);
        Assert.Equal("stateLsn", ValkeyKeys.StateLsn);
    }
}
