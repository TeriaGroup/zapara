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
    public async Task Failed_logins_throttle_the_failing_network_and_never_lock_the_account()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock, throttle: new LoginThrottle(clock));
        await Seed(service);
        const string attacker = "4:198.51.100.7";
        const string owner = "6:20010db8000000010000::/64";
        for (var i = 0; i < LoginThrottle.PairFailureLimit; i++)
            await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login(password: NewPassword), attacker));
        var blocked = await Assert.ThrowsAsync<AccountServiceException>(() => service.LoginAsync(Login(), attacker));
        Assert.Equal(AccountFailure.RateLimited, blocked.Failure);
        Assert.InRange(blocked.RetryAfter!.Value, TimeSpan.FromSeconds(1), LoginThrottle.Window);
        // The stored credential is not locked: the owner signs in from another network right away.
        Assert.Equal(5, await db.ScalarAsync<int>($"SELECT failed_count FROM {db.QuotedSchema}.password_credentials"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.password_credentials WHERE locked_until IS NOT NULL"));
        Assert.NotNull(await service.LoginAsync(Login(), owner, TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.ScalarAsync<int>($"SELECT failed_count FROM {db.QuotedSchema}.password_credentials"));
        // The failing network stays blocked until its window ends, then it may try again.
        await Failure(AccountFailure.RateLimited, () => service.LoginAsync(Login(), attacker));
        clock.Now += LoginThrottle.Window;
        Assert.NotNull(await service.LoginAsync(Login(), attacker, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Known_device_passes_a_network_block_but_not_the_account_cap()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock, throttle: new LoginThrottle(clock));
        await service.RegisterAsync(new("test.user", Password), TestContext.Current.CancellationToken);
        var phone = Guid.NewGuid();
        const string shared = "4:198.51.100.30";
        Assert.NotNull(await service.LoginAsync(Login(device: phone), shared, TestContext.Current.CancellationToken));
        for (var i = 0; i < LoginThrottle.PairFailureLimit; i++)
            await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login(password: NewPassword), shared));
        // A new device on the blocked network waits; the device that signed in before does not.
        await Failure(AccountFailure.RateLimited, () => service.LoginAsync(Login(), shared));
        Assert.NotNull(await service.LoginAsync(Login(device: phone), shared, TestContext.Current.CancellationToken));
        // A device id known for another account gives nothing here.
        await service.RegisterAsync(new("other.user", Password), TestContext.Current.CancellationToken);
        for (var i = 0; i < LoginThrottle.PairFailureLimit; i++)
            await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login("other.user", NewPassword), shared));
        await Failure(AccountFailure.RateLimited, () => service.LoginAsync(Login("other.user", device: phone), shared));
        // The account cap stops everyone, the known device included.
        for (var i = 0; i < LoginThrottle.AccountFailureCap; i++)
            await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login(password: NewPassword), $"4:203.0.{i / 256}.{i % 256}"));
        await Failure(AccountFailure.RateLimited, () => service.LoginAsync(Login(device: phone), shared));
        clock.Now += LoginThrottle.Window;
        Assert.NotNull(await service.LoginAsync(Login(device: phone), shared, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Legacy_locked_until_no_longer_blocks_login()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        await Seed(service);
        await db.ExecuteAsync($"UPDATE {db.QuotedSchema}.password_credentials SET failed_count=5,locked_until='2026-09-08T12:15:00Z'");
        Assert.NotNull(await service.LoginAsync(Login(), TestContext.Current.CancellationToken));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.password_credentials WHERE locked_until IS NOT NULL"));
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
