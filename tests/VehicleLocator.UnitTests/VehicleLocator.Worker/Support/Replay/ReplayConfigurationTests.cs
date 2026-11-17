using VehicleLocator.Worker.Support.Kafka;
using VehicleLocator.Worker.Support.Replay;
using VehicleLocator.Worker.Support.Valkey;

namespace VehicleLocator.UnitTests.VehicleLocator.Worker.Support.Replay;

public class ReplayConfigurationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Replay_desligado_nao_altera_group_ids()
    {
        var kafka = new KafkaOptions();

        ReplayConfiguration.ApplyGroupSuffix(kafka, new ReplayOptions { Enabled = false }, Now);

        Assert.Equal("vehicle-cache-vehicles", kafka.VehiclesGroupId);
        Assert.Equal("vehicle-cache-positions", kafka.PositionsGroupId);
    }

    [Fact]
    public void Replay_ligado_sem_sufixo_gera_group_ids_novos_a_partir_do_relogio()
    {
        var kafka = new KafkaOptions();

        ReplayConfiguration.ApplyGroupSuffix(kafka, new ReplayOptions { Enabled = true }, Now, "run-123");

        Assert.Equal("vehicle-cache-vehicles-replay-20261116120000-run-123", kafka.VehiclesGroupId);
        Assert.Equal("vehicle-cache-positions-replay-20261116120000-run-123", kafka.PositionsGroupId);
    }
    
    [Fact]
    public void Replay_ligado_gera_group_ids_diferentes_em_execucoes_separadas()
    {
        var first = new KafkaOptions();
        var second = new KafkaOptions();
        var replay = new ReplayOptions { Enabled = true };

        ReplayConfiguration.ApplyGroupSuffix(first, replay, Now);
        ReplayConfiguration.ApplyGroupSuffix(second, replay, Now);

        Assert.NotEqual(first.VehiclesGroupId, second.VehiclesGroupId);
        Assert.NotEqual(first.PositionsGroupId, second.PositionsGroupId);
    }

    [Fact]
    public void Replay_ligado_com_sufixo_usa_o_sufixo_informado()
    {
        var kafka = new KafkaOptions();

        ReplayConfiguration.ApplyGroupSuffix(kafka, new ReplayOptions { Enabled = true, GroupSuffix = "abc" }, Now);

        Assert.Equal("vehicle-cache-vehicles-replay-abc", kafka.VehiclesGroupId);
    }

    [Fact]
    public void Replay_no_database_zero_e_recusado()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReplayConfiguration.Validate(new ReplayOptions { Enabled = true }, new ValkeyOptions { Database = 0 }));
    }

    [Fact]
    public void Replay_em_outro_database_e_aceito()
    {
        ReplayConfiguration.Validate(new ReplayOptions { Enabled = true }, new ValkeyOptions { Database = 1 });
    }

    [Fact]
    public void Sem_replay_database_zero_e_aceito()
    {
        ReplayConfiguration.Validate(new ReplayOptions { Enabled = false }, new ValkeyOptions { Database = 0 });
    }
}
