using AwesomeAssertions;
using LunarCal.Infrastructure.Chat;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>FR-QUEUE-5 (P4-R6): the first retries are quick because the user is waiting; later ones back off.</summary>
public sealed class AiQueueRetryPolicyTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 8, 7, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 5, 6)]
    [InlineData(2, 30, 36)]
    [InlineData(3, 180, 216)]
    [InlineData(4, 1080, 1296)]
    public void FR_QUEUE_5_Retry_delays_start_at_5_seconds_and_grow_by_six(int attempt, int minSeconds, int maxSeconds)
    {
        var policy = new AiQueueRetryPolicy();

        for (var i = 0; i < 50; i++)
        {
            var delay = policy.CalculateNextRetry(attempt, _now)!.Value - _now;
            delay.TotalSeconds.Should().BeInRange(minSeconds, maxSeconds);
        }
    }

    [Fact]
    public void FR_QUEUE_5_The_fifth_failure_gives_up_and_waits_for_the_user()
    {
        new AiQueueRetryPolicy().CalculateNextRetry(AiQueueRetryPolicy.MaxRetries, _now).Should().BeNull();
    }
}
