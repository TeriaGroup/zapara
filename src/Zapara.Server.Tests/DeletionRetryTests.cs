using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed class DeletionRetryTests
{
    [Fact]
    public async Task Restarted_host_processes_an_already_queued_deletion_without_another_delete_request()
    {
        await using var harness = await LifecycleHarness.CreateAsync(Console.WriteLine);
        var accounts = new AccountService(harness.Accounts.DataSource, harness.Accounts.Configuration, new AccountClock());
        var session = await AccountTestSupport.Seed(accounts);
        var job = await Queue(harness.Accounts, session.User.UserId);
        await using var host = new LifecycleApiHost(harness);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (await State(harness.Accounts, job) != "completed" && !timeout.IsCancellationRequested)
        {
            try { await Task.Delay(25, timeout.Token); }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { break; }
        }
        Assert.Equal("completed", await State(harness.Accounts, job));
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {harness.Accounts.QuotedSchema}.password_credentials WHERE user_id='{session.User.UserId}'"));
    }

    [Fact]
    public async Task One_failing_deletion_does_not_rollback_other_jobs_and_can_be_retried()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var accounts = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var first = await AccountTestSupport.Seed(accounts, "first.user");
        var second = await AccountTestSupport.Seed(accounts, "second.user");
        var firstJob = await Queue(db, first.User.UserId);
        var secondJob = await Queue(db, second.User.UserId);
        var failure = new FailingParticipant(first.User.UserId);
        var lifecycle = new AccountLifecycleService(db.DataSource, db.Configuration, new AccountClock(),
            [new AccountLifecycleParticipant(db.Configuration), failure], NullLogger<AccountLifecycleService>.Instance);
        await lifecycle.ProcessPendingDeletionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("queued", await State(db, firstJob));
        Assert.Equal("completed", await State(db, secondJob));
        failure.Fail = false;
        await lifecycle.ProcessPendingDeletionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("completed", await State(db, firstJob));
    }

    private static async Task<Guid> Queue(AccountsPostgresFixture db, Guid user)
    {
        var job = Guid.NewGuid();
        await db.ExecuteAsync($"""
            UPDATE {db.QuotedSchema}.users SET status='deleting' WHERE user_id='{user}';
            INSERT INTO {db.QuotedSchema}.deletion_jobs(job_id,user_id,status,created_at) VALUES ('{job}','{user}','queued',now());
            """);
        return job;
    }

    private static Task<string> State(AccountsPostgresFixture db, Guid job)
        => db.ScalarAsync<string>($"SELECT status FROM {db.QuotedSchema}.deletion_jobs WHERE job_id='{job}'");

    [Fact]
    public async Task A_page_of_failing_jobs_does_not_starve_later_deletions()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var ids = Enumerable.Range(1, 33).Select(number => Guid.ParseExact(number.ToString("x32"), "N")).ToArray();
        foreach (var id in ids)
            await db.ExecuteAsync($"""
                INSERT INTO {db.QuotedSchema}.users(user_id,username,normalized_username,created_at,status)
                  VALUES ('{id}','u{id.ToString("N")[24..]}','u{id.ToString("N")[24..]}',now(),'deleting');
                INSERT INTO {db.QuotedSchema}.deletion_jobs(job_id,user_id,status,created_at)
                  VALUES ('{id}','{id}','queued',now());
                """);
        var lifecycle = new AccountLifecycleService(db.DataSource, db.Configuration, new AccountClock(),
            [new FailingPage(ids[^1])], NullLogger<AccountLifecycleService>.Instance);
        await lifecycle.ProcessPendingDeletionsAsync(TestContext.Current.CancellationToken);
        await lifecycle.ProcessPendingDeletionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("completed", await State(db, ids[^1]));
        Assert.Equal(32L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.deletion_jobs WHERE status='queued'"));
    }

    private sealed class FailingPage(Guid last) : IAccountLifecycleParticipant
    {
        public string Module => "synthetic";
        public Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
            => context.UserId == last ? Task.CompletedTask : Task.FromException(new IOException("Synthetic persistent failure"));
    }

    [Fact]
    public async Task Cancelled_first_job_does_not_skip_the_unattempted_rest_of_its_page()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var ids = Enumerable.Range(1, 33).Select(number => Guid.ParseExact(number.ToString("x32"), "N")).ToArray();
        foreach (var id in ids)
            await db.ExecuteAsync($"""
                INSERT INTO {db.QuotedSchema}.users(user_id,username,normalized_username,created_at,status)
                  VALUES ('{id}','u{id.ToString("N")[24..]}','u{id.ToString("N")[24..]}',now(),'deleting');
                INSERT INTO {db.QuotedSchema}.deletion_jobs(job_id,user_id,status,created_at)
                  VALUES ('{id}','{id}','queued',now());
                """);
        var participant = new SlowFirstJob(ids[0]);
        var lifecycle = new AccountLifecycleService(db.DataSource, db.Configuration, new AccountClock(),
            [participant], NullLogger<AccountLifecycleService>.Instance);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var firstAttempt = lifecycle.ProcessPendingDeletionsAsync(cancelled.Token);
        await participant.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstAttempt);
        await lifecycle.ProcessPendingDeletionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("queued", await State(db, ids[0]));
        foreach (var job in ids.Skip(1)) Assert.Equal("completed", await State(db, job));
    }

    private sealed class SlowFirstJob(Guid first) : IAccountLifecycleParticipant
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Module => "synthetic";
        public Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
        {
            if (context.UserId != first) return Task.CompletedTask;
            Entered.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class FailingParticipant(Guid user) : IAccountLifecycleParticipant
    {
        public bool Fail { get; set; } = true;
        public string Module => "synthetic";
        public Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
            => Fail && context.UserId == user ? Task.FromException(new IOException("Synthetic participant failure")) : Task.CompletedTask;
    }
}
