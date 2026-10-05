using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed partial class AccountSessionTests
{
    [Fact]
    public async Task Rotation_replaces_access_replay_commits_revocation_and_unknown_has_no_collateral()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var old = await Seed(service);
        var other = await service.LoginAsync(Login(), TestContext.Current.CancellationToken);
        var next = await service.RefreshAsync(old.RefreshToken, TestContext.Current.CancellationToken);
        Assert.NotNull(next);
        Assert.Equal(old.FamilyId, next.FamilyId);
        Assert.Equal(old.RefreshExpiresAt, next.RefreshExpiresAt);
        Assert.True(old.AccessToken != next.AccessToken && old.RefreshToken != next.RefreshToken);
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(old.AccessToken));
        Assert.NotNull(await service.AuthenticateAsync(next.AccessToken, TestContext.Current.CancellationToken));
        Assert.NotNull(await service.AuthenticateAsync(next.AccessToken, TestContext.Current.CancellationToken));
        await Failure(AccountFailure.InvalidSession, () => service.RefreshAsync("zr_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')));
        Assert.NotNull(await service.AuthenticateAsync(next.AccessToken, TestContext.Current.CancellationToken));
        await Failure(AccountFailure.InvalidSession, () => service.RefreshAsync(old.RefreshToken));
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(next.AccessToken));
        Assert.NotNull(await service.AuthenticateAsync(other.AccessToken, TestContext.Current.CancellationToken));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='refresh_replay'"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(next.AccessToken)));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.access_tokens WHERE token_hash=decode('{hash}','hex')"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE octet_length(token_hash)<>32"));
    }

    [Fact]
    public async Task Concurrent_refresh_has_one_winner_but_replay_revokes_winner()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var old = await Seed(service);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return await service.RefreshAsync(old.RefreshToken); }
            catch (AccountServiceException e) { Assert.Equal(AccountFailure.InvalidSession, e.Failure); return null; }
        }));
        var winner = Assert.Single(results, x => x is not null)!;
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(winner.AccessToken));
        await Failure(AccountFailure.InvalidSession, () => service.RefreshAsync(winner.RefreshToken));
    }

    [Fact]
    public async Task Access_and_absolute_family_expiry_reject_at_exact_boundaries()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock);
        var old = await Seed(service);
        clock.Now = old.AccessExpiresAt;
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(old.AccessToken));
        clock.Now = old.RefreshExpiresAt - TimeSpan.FromMinutes(1);
        var next = await service.RefreshAsync(old.RefreshToken, TestContext.Current.CancellationToken);
        Assert.Equal(old.RefreshExpiresAt, next.AccessExpiresAt);
        clock.Now = old.RefreshExpiresAt;
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(next.AccessToken));
        await Failure(AccountFailure.InvalidSession, () => service.RefreshAsync(next.RefreshToken));
    }

    [Fact]
    public async Task Profile_devices_cursor_and_owned_revocation_are_account_scoped()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var a = await Seed(service);
        var b = await Seed(service, "other.user");
        var extra = await service.LoginAsync(Login(), TestContext.Current.CancellationToken);
        Assert.Equal("Имя", (await service.UpdateProfileAsync(a.AccessToken, new("Имя"), TestContext.Current.CancellationToken)).DisplayName);
        Assert.Equal("Имя", (await service.GetMeAsync(a.AccessToken, TestContext.Current.CancellationToken)).User.DisplayName);
        Assert.Null((await service.UpdateProfileAsync(a.AccessToken, new(null), TestContext.Current.CancellationToken)).DisplayName);
        var page = await service.ListDevicesAsync(a.AccessToken, 1, ct: TestContext.Current.CancellationToken);
        Assert.Single(page.Devices);
        Assert.NotNull(page.NextCursor);
        var next = await service.ListDevicesAsync(a.AccessToken, 1, page.NextCursor, TestContext.Current.CancellationToken);
        Assert.Single(next.Devices);
        Assert.NotEqual(page.Devices[0].FamilyId, next.Devices[0].FamilyId);
        Assert.Null(next.NextCursor);
        await Failure(AccountFailure.InvalidRequest, () => service.ListDevicesAsync(b.AccessToken, 1, page.NextCursor));
        await Failure(AccountFailure.InvalidRequest, () => service.ListDevicesAsync(a.AccessToken, 101));
        await Failure(AccountFailure.InvalidRequest, () => service.ListDevicesAsync(a.AccessToken, 20, "invalid"));
        await Failure(AccountFailure.SessionNotFound, () => service.RevokeSessionAsync(a.AccessToken, b.FamilyId));
        await Failure(AccountFailure.SessionNotFound, () => service.RevokeSessionAsync(a.AccessToken, Guid.NewGuid()));
        await service.RevokeSessionAsync(a.AccessToken, extra.FamilyId, TestContext.Current.CancellationToken);
        await service.RevokeSessionAsync(a.AccessToken, extra.FamilyId, TestContext.Current.CancellationToken);
        Assert.Single((await service.ListDevicesAsync(a.AccessToken, ct: TestContext.Current.CancellationToken)).Devices);
        await service.RevokeAllAsync(a.AccessToken, TestContext.Current.CancellationToken);
        await Failure(AccountFailure.InvalidSession, () => service.UpdateProfileAsync(a.AccessToken, new("Не менять")));
        Assert.NotNull(await service.GetMeAsync(b.AccessToken, TestContext.Current.CancellationToken));
        await service.LogoutAsync(b.AccessToken, TestContext.Current.CancellationToken);
        await Failure(AccountFailure.InvalidSession, () => service.GetMeAsync(b.AccessToken));
    }
}
