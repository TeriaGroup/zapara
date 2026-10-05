using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Web;

namespace Zapara.Server.Tests;

public sealed class TeacherRefreshWorkerTests
{
    private static TeacherRefreshPolicy Policy(params (string Key, string Value)[] entries) => TeacherRefreshPolicy.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(entries.ToDictionary(e => "Teachers:Refresh:" + e.Key, e => (string?)e.Value)).Build());

    [Fact]
    public async Task Disabled_worker_performs_no_fetch_and_enabled_worker_recovers_after_capped_backoff()
    {
        var calls = 0;
        await Policy().RunAsync(_ => { calls++; return Task.FromResult(true); }, (_, _) => throw new InvalidOperationException(), TestContext.Current.CancellationToken);
        Assert.Equal(0, calls);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waits = new List<TimeSpan>();
        var enabled = Policy(("Enabled", "true"), ("MaximumRetrySeconds", "60"), ("IntervalMinutes", "20"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enabled.RunAsync(_ => Task.FromResult(++calls == 4), (wait, token) =>
        {
            waits.Add(wait);
            if (waits.Count == 6) stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, stop.Token));
        Assert.Equal(5, calls);
        Assert.Equal(new[] { 5d, 30d, 60d, 60d, 1200d, 30d }, waits.Select(time => time.TotalSeconds));
    }

    [Theory]
    [InlineData("Enabled", "yes")]
    [InlineData("IntervalMinutes", "1")]
    [InlineData("DeadlineSeconds", "121")]
    [InlineData("RetrySeconds", "0")]
    public void Invalid_or_aggressive_refresh_configuration_is_rejected(string key, string value)
    {
        Assert.Throws<ArgumentException>(() => Policy(("Enabled", key == "Enabled" ? value : "true"),
            (key == "Enabled" ? "IntervalMinutes" : key, key == "Enabled" ? "60" : value)));
    }
}
