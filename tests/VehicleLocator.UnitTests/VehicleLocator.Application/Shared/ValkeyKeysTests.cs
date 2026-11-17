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
    public void State_field_names_are_stable()
    {
        Assert.Equal("state", ValkeyKeys.State);
        Assert.Equal("stateTs", ValkeyKeys.StateTs);
        Assert.Equal("stateLsn", ValkeyKeys.StateLsn);
    }

    [Fact]
    public void Position_field_names_are_stable()
    {
        Assert.Equal("lat", ValkeyKeys.Lat);
        Assert.Equal("lng", ValkeyKeys.Lng);
        Assert.Equal("posTs", ValkeyKeys.PosTs);
        Assert.Equal("posLsn", ValkeyKeys.PosLsn);
    }

    [Fact]
    public void State_and_position_slices_never_share_a_field()
    {
        string[] state = [ValkeyKeys.State, ValkeyKeys.StateTs, ValkeyKeys.StateLsn];
        string[] position = [ValkeyKeys.Lat, ValkeyKeys.Lng, ValkeyKeys.PosTs, ValkeyKeys.PosLsn];

        Assert.Empty(state.Intersect(position));
    }
}
