using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class TimetableRefreshWorkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static TimetableRefreshPolicy Policy(params (string Key, string Value)[] entries) => TimetableRefreshPolicy.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(entries.ToDictionary(e => "Timetable:Refresh:" + e.Key, e => (string?)e.Value)).Build());

    [Fact]
    public void Worker_is_opt_in_and_invalid_or_aggressive_configuration_is_rejected()
    {
        Assert.False(Policy().Enabled);
        Assert.True(Policy(("Enabled", "true")).Enabled);
        Assert.Throws<ArgumentException>(() => Policy(("Enabled", "true"), ("IntervalMinutes", "1")));
        Assert.Throws<ArgumentException>(() => Policy(("Enabled", "yes")));
    }

    [Fact]
    public void Failed_refreshes_back_off_with_a_cap_and_success_restores_interval()
    {
        var policy = Policy(("Enabled", "true"));
        var failed = IngestResult.Failed(FailureCode.SourceRejected);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay(failed, 1));
        Assert.Equal(TimeSpan.FromSeconds(60), policy.NextDelay(failed, 2));
        Assert.Equal(TimeSpan.FromMinutes(15), policy.NextDelay(failed, 100));
        Assert.Equal(TimeSpan.FromHours(1), policy.NextDelay(new(0), 0));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay(new(3), 0));
    }

    [Fact]
    public async Task Ambiguous_commit_stops_worker_without_blind_retry()
    {
        var calls = 0;
        var observations = new List<IngestResult>();
        await Policy(("Enabled", "true")).RunAsync(_ =>
        {
            calls++;
            return Task.FromResult(IngestResult.Failed(FailureCode.PublicationUnknown));
        }, (_, _) => Task.CompletedTask, observations.Add, Ct);
        Assert.Equal(1, calls);
        Assert.Equal(FailureCode.PublicationUnknown, Assert.Single(observations).FailureCode);
    }

    [Fact]
    public async Task Cancellation_during_wait_never_starts_another_refresh()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var calls = 0;
        var waits = new List<TimeSpan>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Policy(("Enabled", "true")).RunAsync(_ =>
        {
            calls++;
            return Task.FromResult(IngestResult.Failed(FailureCode.SourceRejected));
        }, (wait, token) =>
        {
            waits.Add(wait);
            if (waits.Count == 3) stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, _ => { }, stop.Token));
        Assert.Equal(2, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) }, waits);
    }
}
