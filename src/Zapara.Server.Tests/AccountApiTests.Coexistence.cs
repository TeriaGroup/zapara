using System.Net;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed partial class AccountApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACC07_Anonymous_timetable_with_enabled_accounts_and_bad_bearer(bool badAccountStorage)
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var accounts = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(accounts, overrides: badAccountStorage
            ? new() { ["ConnectionStrings:Accounts"] = "bad-account-dsn-canary" } : null, timetable: timetable, clock: timetable.Clock);
        host.Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer za_" + new string('A', 43));
        using var live = await host.Client.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        await ApiTestFactory.ProblemAsync(host.Client, "/api/v1/groups", HttpStatusCode.ServiceUnavailable, "snapshot_unavailable");
        await ApiTestFactory.ProblemAsync(host.Client, "/api/v1/groups?snapshotId=bad", HttpStatusCode.BadRequest, "invalid_snapshot_id");
        var id = await ApiTestFactory.PublishAsync(timetable);
        await using (var lease = await timetable.Store.TryAcquireAsync(Ct))
        {
            Assert.NotNull(lease);
            await timetable.Store.RecordFailedAttemptAsync(lease, FailureCode.SnapshotMalformed, Ct);
        }
        var before = await ApiTestFactory.DatabaseStateAsync(timetable);
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/groups?snapshotId=" + id);
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/groups/3313/timetable?snapshotId=" + id);
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/status");
        using var ready = await host.Client.GetAsync("/health/ready", Ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var ingest = await host.Client.PostAsync("/api/v1/ingest", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, ingest.StatusCode);
        Assert.True(before == await ApiTestFactory.DatabaseStateAsync(timetable));
        await timetable.Store.EnsureSchemaAsync(Ct);
        Assert.DoesNotContain("bad-account-dsn-canary", string.Join('\n', host.Logs));
    }

    [Fact]
    public async Task Missing_schema_is_not_created_and_invalid_enablement_fails_closed()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        await using (var host = new AccountApiTestHost(db))
        {
            await host.Send("POST", "/auth/login", 503, LoginBody(), code: "db_unavailable");
            await host.Send("GET", "/account/me", 503, bearer: "za_" + new string('A', 43), code: "db_unavailable");
        }
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_class WHERE relnamespace='{db.Schema}'::regnamespace"));
        Assert.Throws<ArgumentException>(() => new AccountApiTestHost(db, overrides: new() { ["Accounts:Enabled"] = "not-boolean" }));
    }

    [Fact]
    public async Task ACC10_Unexpected_exception_uses_existing_safe_handler_in_Development()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db, "Development", clock: new ExceptionClock());
        await host.Send("POST", "/auth/register", 500, new { username = "synthetic", password = Password }, code: "internal_error");
        Assert.DoesNotContain("exception-secret-canary", string.Join('\n', host.Logs));
    }

    private sealed class ExceptionClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("exception-secret-canary");
    }
}
