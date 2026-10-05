using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed partial class PasswordAccountTests
{
    [Fact]
    public async Task Registration_race_is_atomic_and_hashes_are_salted_framework_hashes()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try { return (User: await service.RegisterAsync(new(i == 0 ? "TEST.user" : "test.user", Password)), Error: (AccountFailure?)null); }
            catch (AccountServiceException e) { return (User: (UserResponse?)null, Error: (AccountFailure?)e.Failure); }
        }));
        Assert.Single(attempts, x => x.User is not null);
        Assert.Single(attempts, x => x.Error == AccountFailure.UsernameUnavailable);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.password_credentials"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='register'"));
        var first = attempts.Single(x => x.User is not null).User!;
        var second = await service.RegisterAsync(new("another.user", Password), TestContext.Current.CancellationToken);
        var hash = await db.ScalarAsync<string>($"SELECT password_hash FROM {db.QuotedSchema}.password_credentials WHERE user_id='{first.UserId}'");
        var hash2 = await db.ScalarAsync<string>($"SELECT password_hash FROM {db.QuotedSchema}.password_credentials WHERE user_id='{second.UserId}'");
        Assert.True(hash != hash2 && hash != Password);
        var hasher = new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions { IterationCount = 100000, CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3 }));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(new(first.UserId), hash, Password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(new(first.UserId), hash, NewPassword));
        await using var reopened = db.Configuration.CreateDataSource();
        Assert.NotNull(await new AccountService(reopened, db.Configuration, new AccountClock()).LoginAsync(Login("test.USER"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lockout_is_persisted_fixed_nonextending_and_success_clears()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock);
        await Seed(service);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Failure(AccountFailure.InvalidCredentials,
            () => service.LoginAsync(Login(password: NewPassword)))));
        Assert.Equal(5, await db.ScalarAsync<int>($"SELECT failed_count FROM {db.QuotedSchema}.password_credentials"));
        var locked = await db.ScalarAsync<DateTime>($"SELECT locked_until FROM {db.QuotedSchema}.password_credentials");
        clock.Now += TimeSpan.FromMinutes(14);
        await using var reopened = db.Configuration.CreateDataSource();
        await Failure(AccountFailure.InvalidCredentials, () => new AccountService(reopened, db.Configuration, clock).LoginAsync(Login()));
        Assert.Equal(locked, await db.ScalarAsync<DateTime>($"SELECT locked_until FROM {db.QuotedSchema}.password_credentials"));
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.NotNull(await service.LoginAsync(Login(), TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.ScalarAsync<int>($"SELECT failed_count FROM {db.QuotedSchema}.password_credentials"));
    }

    [Fact]
    public async Task Password_change_revokes_all_and_rejects_old_password_and_inactive_users()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(service);
        var second = await service.LoginAsync(Login(), TestContext.Current.CancellationToken);
        await Failure(AccountFailure.InvalidCredentials, () => service.ChangePasswordAsync(session.AccessToken, new(NewPassword, Password)));
        await service.ChangePasswordAsync(session.AccessToken, new(Password, NewPassword), TestContext.Current.CancellationToken);
        foreach (var s in new[] { session, second })
        {
            await Failure(AccountFailure.InvalidSession, () => service.GetMeAsync(s.AccessToken));
            await Failure(AccountFailure.InvalidSession, () => service.RefreshAsync(s.RefreshToken));
        }
        await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login()));
        var fresh = await service.LoginAsync(Login(password: NewPassword), TestContext.Current.CancellationToken);
        Assert.Equal(2, (await service.AuthenticateAsync(fresh.AccessToken, TestContext.Current.CancellationToken)).CredentialVersion);
        foreach (var status in new[] { "disabled", "deleting" })
        {
            await db.ExecuteAsync($"UPDATE {db.QuotedSchema}.users SET status='{status}'");
            await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login(password: NewPassword)));
            await Failure(AccountFailure.InvalidSession, () => service.GetMeAsync(fresh.AccessToken));
        }
    }
}
