namespace VehicleLocator.Application.Shared;

public sealed class OptimisticWriteExhaustedException(int attempts) : Exception($"Optimistic write did not commit after {attempts} attempts");

public static class OptimisticWrite
{
    /// <summary>
    /// Runs an asynchronous operation until it commits or the maximum number of attempts is reached.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by a successful attempt.</typeparam>
    /// <param name="attempt">An asynchronous operation that returns whether it committed and its result.</param>
    /// <param name="maxAttempts">The maximum number of attempts to run.</param>
    /// <returns>The result from the first committed attempt.</returns>
    /// <exception cref="OptimisticWriteExhaustedException">The maximum number of attempts is reached without a successful commit.</exception>
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
