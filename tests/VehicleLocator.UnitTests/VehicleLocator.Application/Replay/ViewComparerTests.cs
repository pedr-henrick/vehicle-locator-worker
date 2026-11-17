using VehicleLocator.Application.Replay;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Replay;

public class ViewComparerTests
{
    private static ViewSnapshot Snap(params (string Key, (string Field, string Value)[] Fields)[] entries) =>
        new(entries.ToDictionary(
            e => e.Key,
            e => (IReadOnlyDictionary<string, string>)e.Fields.ToDictionary(f => f.Field, f => f.Value)));

    [Fact]
    public void Snapshots_iguais_sao_identicos()
    {
        var a = Snap(("vehicle:1", [("state", "ACTIVE"), ("stateTs", "10")]));
        var b = Snap(("vehicle:1", [("state", "ACTIVE"), ("stateTs", "10")]));

        Assert.True(ViewComparer.Compare(a, b).IsIdentical);
    }

    [Fact]
    public void Ordem_dos_campos_nao_importa()
    {
        var a = Snap(("vehicle:1", [("state", "ACTIVE"), ("stateTs", "10")]));
        var b = Snap(("vehicle:1", [("stateTs", "10"), ("state", "ACTIVE")]));

        Assert.True(ViewComparer.Compare(a, b).IsIdentical);
    }

    [Fact]
    public void Chave_so_na_origem_e_reportada()
    {
        var a = Snap(("vehicle:1", [("state", "ACTIVE")]), ("deleted:2", [("value", "1")]));
        var b = Snap(("vehicle:1", [("state", "ACTIVE")]));

        var diff = ViewComparer.Compare(a, b);

        Assert.Equal(["deleted:2"], diff.OnlyInSource);
        Assert.Empty(diff.OnlyInReplay);
        Assert.False(diff.IsIdentical);
    }

    [Fact]
    public void Chave_so_no_replay_e_reportada()
    {
        var a = Snap(("vehicle:1", [("state", "ACTIVE")]));
        var b = Snap(("vehicle:1", [("state", "ACTIVE")]), ("vehicle:9", [("lat", "1")]));

        var diff = ViewComparer.Compare(a, b);

        Assert.Equal(["vehicle:9"], diff.OnlyInReplay);
        Assert.False(diff.IsIdentical);
    }

    [Fact]
    public void Valor_diferente_e_reportado()
    {
        var a = Snap(("vehicle:1", [("state", "ACTIVE")]));
        var b = Snap(("vehicle:1", [("state", "IDLE")]));

        Assert.Equal(["vehicle:1"], ViewComparer.Compare(a, b).Different);
    }

    [Fact]
    public void Campo_a_mais_ou_a_menos_e_diferenca()
    {
        var a = Snap(("vehicle:1", [("state", "ACTIVE"), ("lat", "1")]));
        var b = Snap(("vehicle:1", [("state", "ACTIVE")]));

        Assert.Equal(["vehicle:1"], ViewComparer.Compare(a, b).Different);
    }
}
