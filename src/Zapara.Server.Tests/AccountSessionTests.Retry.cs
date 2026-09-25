using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class AccountSessionTests
{
    [Fact]
    public async Task Same_refresh_attempt_can_resume_after_access_expiry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock);
        var initial = await Seed(service);
        var attempt = Guid.NewGuid();
        var first = await service.RefreshRetryAsync(initial.RefreshToken, attempt, ct);

        clock.Now = first.AccessExpiresAt;
        var recovered = await service.RefreshRetryAsync(initial.RefreshToken, attempt, ct);
        Assert.Equal(first, recovered);
        await Failure(AccountFailure.InvalidSession,
            () => service.AuthenticateAsync(recovered.AccessToken, ct));

        var fresh = await service.RefreshRetryAsync(recovered.RefreshToken, Guid.NewGuid(), ct);
        Assert.NotNull(await service.AuthenticateAsync(fresh.AccessToken, ct));
        Assert.Equal(initial.FamilyId, fresh.FamilyId);
    }

    [Fact]
    public async Task Different_attempt_replay_revokes_family_but_late_same_attempt_does_not()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var initial = await Seed(service);
        var firstAttempt = Guid.NewGuid();
        var first = await service.RefreshRetryAsync(initial.RefreshToken, firstAttempt, ct);
        var second = await service.RefreshRetryAsync(first.RefreshToken, Guid.NewGuid(), ct);

        await Failure(AccountFailure.InvalidSession,
            () => service.RefreshRetryAsync(initial.RefreshToken, firstAttempt, ct));
        Assert.NotNull(await service.AuthenticateAsync(second.AccessToken, ct));

        await Failure(AccountFailure.InvalidSession,
            () => service.RefreshRetryAsync(first.RefreshToken, Guid.NewGuid(), ct));
        await Failure(AccountFailure.InvalidSession,
            () => service.AuthenticateAsync(second.AccessToken, ct));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='refresh_replay'"));
    }

    [Fact]
    public async Task Concurrent_same_attempt_returns_one_replacement_without_revocation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var initial = await Seed(service);
        var attempt = Guid.NewGuid();

        var results = await Task.WhenAll(
            service.RefreshRetryAsync(initial.RefreshToken, attempt, ct),
            service.RefreshRetryAsync(initial.RefreshToken, attempt, ct));

        Assert.Equal(results[0], results[1]);
        Assert.NotNull(await service.AuthenticateAsync(results[0].AccessToken, ct));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='refresh_replay'"));
    }
}
