using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class AccountServiceTests
{
    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("failed_login")]
    [InlineData("logout")]
    [InlineData("revoke")]
    [InlineData("revoke_all")]
    [InlineData("password_change")]
    [InlineData("refresh_replay")]
    public async Task Audit_failure_rolls_back_every_security_mutation(string action)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(service);
        var target = await service.LoginAsync(Login(), TestContext.Current.CancellationToken);
        if (action == "refresh_replay") await service.RefreshAsync(session.RefreshToken, TestContext.Current.CancellationToken);
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.reject_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic audit rejection'; END $$;
            CREATE TRIGGER reject_audit BEFORE INSERT ON {db.QuotedSchema}.account_security_events
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.reject_audit()
            """);
        var before = await AccountState(db);
        await Failure(AccountFailure.DbUnavailable, () => action switch
        {
            "register" => service.RegisterAsync(new("rollback.user", Password)),
            "login" => service.LoginAsync(Login()),
            "failed_login" => service.LoginAsync(Login(password: NewPassword)),
            "logout" => service.LogoutAsync(session.AccessToken),
            "revoke" => service.RevokeSessionAsync(session.AccessToken, target.FamilyId),
            "revoke_all" => service.RevokeAllAsync(session.AccessToken),
            "password_change" => service.ChangePasswordAsync(session.AccessToken, new(Password, NewPassword)),
            "refresh_replay" => service.RefreshAsync(session.RefreshToken),
            _ => throw new InvalidOperationException("Unknown fixture case.")
        });
        Assert.True(before == await AccountState(db), "Audit failure must preserve all account row state.");
    }

    [Fact]
    public async Task Commit_cancellation_is_not_reported_as_success_or_retried()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.slow_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_sleep(10); RETURN NEW; END $$;
            CREATE CONSTRAINT TRIGGER slow_commit AFTER INSERT ON {db.QuotedSchema}.account_security_events
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.slow_commit()
            """);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var registration = service.RegisterAsync(new("commit.user", Password), cancel.Token);
        // During COMMIT the active query is COMMIT (not schema-qualified).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (await db.ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND query='COMMIT' AND wait_event='PgSleep'") == 0)
            await Task.Delay(20, timeout.Token);
        cancel.Cancel();
        await Failure(AccountFailure.DbUnavailable, () => registration);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task Missing_schema_is_database_failure_not_invalid_session_and_runtime_never_migrates()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        await Failure(AccountFailure.DbUnavailable, () => service.AuthenticateAsync("za_" + new string('A', 43)));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
    }

    private static Task<string> AccountState(AccountsPostgresFixture db)
    {
        var tables = new[] { "users", "password_credentials", "session_families", "access_tokens", "refresh_tokens", "account_security_events" };
        return db.ScalarAsync<string>("SELECT jsonb_build_array(" + string.Join(",", tables.Select(t =>
            $"(SELECT jsonb_agg(to_jsonb(x) ORDER BY to_jsonb(x)::text) FROM {db.QuotedSchema}.{t} x)")) + ")::text");
    }
}
