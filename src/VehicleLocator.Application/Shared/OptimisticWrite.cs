namespace VehicleLocator.Application.Shared
{
    public sealed class OptimisticWriteExhaustedException(int attempts)
    : Exception($"Optimistic write did not commit after {attempts} attempts");

    public static class OptimisticWrite
    {
        /// <summary>
        /// Repete a tentativa enquanto a transação do Valkey for abortada por conflito.
        /// A tentativa devolve (committed, resultado). Esgotadas as tentativas, lança
        /// exceção: o consumer não commita o offset e o evento é reprocessado.
        /// </summary>
        public static async Task<T> RunAsync<T>(
            Func<Task<(bool Committed, T Result)>> attempt,
            int maxAttempts = 5)
        {
            for (var i = 0; i < maxAttempts; i++)
            {
                var (committed, result) = await attempt();
                if (committed) return result;
            }

            throw new OptimisticWriteExhaustedException(maxAttempts);
        }
    }
}
