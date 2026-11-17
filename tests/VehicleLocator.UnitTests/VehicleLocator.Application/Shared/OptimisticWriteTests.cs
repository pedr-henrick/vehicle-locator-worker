using VehicleLocator.Application.Shared;

namespace VehicleLocator.UnitTests.VehicleLocator.Application.Shared;

public class OptimisticWriteTests
{
    [Fact]
    public async Task Returns_result_when_first_attempt_commits()
    {
        var attempts = 0;

        var result = await OptimisticWrite.RunAsync(() =>
        {
            attempts++;
            return Task.FromResult((true, "ok"));
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Retries_until_transaction_commits()
    {
        var attempts = 0;

        var result = await OptimisticWrite.RunAsync(() =>
        {
            attempts++;
            return Task.FromResult((attempts == 3, attempts));
        });

        Assert.Equal(3, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Throws_when_attempts_are_exhausted()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<OptimisticWriteExhaustedException>(() =>
            OptimisticWrite.RunAsync(() =>
            {
                attempts++;
                return Task.FromResult((false, 0));
            }, maxAttempts: 4));

        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task Default_limit_is_five_attempts()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<OptimisticWriteExhaustedException>(() =>
            OptimisticWrite.RunAsync(() =>
            {
                attempts++;
                return Task.FromResult((false, 0));
            }));

        Assert.Equal(5, attempts);
    }
}
