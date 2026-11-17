using VehicleLocator.MockWriters;
using Xunit;

namespace VehicleLocator.UnitTests.VehicleLocator.MockWriters;

public class StreamOptionsTests
{
    [Fact]
    public void Parse_WithoutArguments_ReturnsDefault()
    {
        Assert.Equal(StreamOptions.Default, StreamOptions.Parse([]));
    }

    [Fact]
    public void Parse_OverridesOnlyTheGivenKeys()
    {
        var options = StreamOptions.Parse(["rate=40", "seed=7", "ooo=0"]);

        Assert.Equal(40, options.RatePerSecond);
        Assert.Equal(7, options.Seed);
        Assert.Equal(0, options.OutOfOrderPercent);
        Assert.Equal(StreamOptions.Default.Vehicles, options.Vehicles);
    }

    [Theory]
    [InlineData("foo=1")]
    [InlineData("rate")]
    [InlineData("rate=abc")]
    [InlineData("vehicles=0")]
    [InlineData("positions=0")]
    [InlineData("transitions=0")]
    [InlineData("rate=-1")]
    [InlineData("ooo=101")]
    [InlineData("dup=-1")]
    public void Parse_InvalidArgument_Throws(string pair)
    {
        Assert.Throws<ArgumentException>(() => StreamOptions.Parse([pair]));
    }
}
