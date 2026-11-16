using VehicleLocator.Application.Vehicles;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Vehicles
{
    public class VehicleDeciderTests
    {
        // curTs/curLsn null = sem hash no cache
        [Theory]
        [InlineData(null, null, false, VehicleChangeKind.Upsert, 100L, 10L, VehicleDecision.Apply)]          // primeiro evento
        [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 200L, 20L, VehicleDecision.Apply)]           // mais novo
        [InlineData(200L, 20L, false, VehicleChangeKind.Upsert, 100L, 30L, VehicleDecision.IgnoreStale)]     // ts antigo, mesmo com LSN maior
        [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 100L, 11L, VehicleDecision.Apply)]           // empate de ts, LSN maior
        [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 100L, 9L, VehicleDecision.IgnoreStale)]      // empate de ts, LSN menor
        [InlineData(100L, 10L, false, VehicleChangeKind.Upsert, 100L, 10L, VehicleDecision.IgnoreDuplicate)] // reentrega
        [InlineData(null, null, true, VehicleChangeKind.Upsert, 999L, 99L, VehicleDecision.IgnoreDeleted)]   // evento após delete
        [InlineData(200L, 20L, false, VehicleChangeKind.Delete, 100L, 30L, VehicleDecision.Remove)]          // delete com updated_at antigo ainda vence
        [InlineData(null, null, false, VehicleChangeKind.Delete, 100L, 30L, VehicleDecision.Remove)]         // delete de veículo nunca visto
        [InlineData(null, null, true, VehicleChangeKind.Delete, 100L, 30L, VehicleDecision.IgnoreDuplicate)] // delete reentregue
        public void Decide(
            long? curTs, long? curLsn, bool isDeleted,
            VehicleChangeKind kind, long ts, long lsn,
            VehicleDecision expected)
        {
            var current = curTs is null ? null : new VehicleViewState("ACTIVE", curTs.Value, curLsn!.Value);
            var change = new VehicleChange("v-1", kind, kind == VehicleChangeKind.Upsert ? "IDLE" : null, ts, lsn);

            Assert.Equal(expected, VehicleDecider.Decide(current, isDeleted, change));
        }
    }
}
