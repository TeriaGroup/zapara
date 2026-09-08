using System.Text.Json;
using Xunit;
using Zapara.Contracts.Accounts;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed partial class AccountApiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ACC01_Register_login_me_rotate_and_replay()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        var user = await host.Register();
        ApiTestFactory.Keys(user, "userId", "username", "displayName", "createdAt");
        await host.Send("POST", "/auth/register", 409, new RegisterRequest("SYNTHETIC", Password), code: "username_unavailable");
        var session = await host.Login();
        var me = await host.Send("GET", "/account/me", 200, bearer: session.AccessToken);
        ApiTestFactory.Keys(me, "user", "familyId", "authenticationMethods");
        Assert.Equal(user.GetProperty("userId").GetGuid(), me.GetProperty("user").GetProperty("userId").GetGuid());
        Assert.Equal("password", me.GetProperty("authenticationMethods")[0].GetString());
        var winner = await host.Refresh(session.RefreshToken);
        Assert.True(session.RefreshToken != winner.RefreshToken);
        await host.InvalidAccess(session.AccessToken);
        await host.Send("GET", "/account/me", 200, bearer: winner.AccessToken);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE consumed_at IS NOT NULL"));
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.access_tokens WHERE octet_length(token_hash)=32"));
        await host.Send("POST", "/auth/refresh", 401, new { refreshToken = session.RefreshToken }, code: "invalid_session");
        await host.InvalidAccess(winner.AccessToken);
        await host.Send("POST", "/auth/refresh", 401, new { refreshToken = winner.RefreshToken }, code: "invalid_session");
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='refresh_replay'"));
        var logs = string.Join('\n', host.Logs);
        foreach (var secret in new[] { Password, session.AccessToken, session.RefreshToken, winner.AccessToken, winner.RefreshToken,
                     Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")! })
            Assert.True(!logs.Contains(secret), "Application log credential canary must be absent.");
    }

    [Fact]
    public async Task ACC04_ACC09_Devices_profile_foreign_revoke_and_idempotence()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var first = await host.Login();
        var second = await host.Login();
        await host.Register("foreign");
        var foreign = await host.Login("foreign");
        var devices = await host.Send("GET", "/account/devices?limit=1", 200, bearer: first.AccessToken);
        Assert.Single(devices.GetProperty("devices").EnumerateArray());
        var cursor = devices.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        var next = await host.Send("GET", "/account/devices?limit=1&cursor=" + cursor, 200, bearer: first.AccessToken);
        Assert.Single(next.GetProperty("devices").EnumerateArray());
        await host.Send("GET", "/account/devices?cursor=" + cursor, 400, bearer: foreign.AccessToken, code: "invalid_request");
        foreach (var target in new[] { foreign.FamilyId, Guid.NewGuid() })
            await host.Send("DELETE", "/account/devices/" + target, 404, bearer: first.AccessToken, code: "session_not_found");
        await host.Send("PATCH", "/account/me", 200, new UpdateProfileRequest("Имя"), first.AccessToken);
        var cleared = await host.Send("PATCH", "/account/me", 200, new UpdateProfileRequest(null), first.AccessToken);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("displayName").ValueKind);
        for (var i = 0; i < 2; i++)
            await host.Send("DELETE", "/account/devices/" + second.FamilyId, 204, bearer: first.AccessToken);
        await host.Send("PATCH", "/account/me", 401, new UpdateProfileRequest("Rejected"), second.AccessToken, code: "invalid_session");
        await host.Send("GET", "/account/me", 200, bearer: foreign.AccessToken);
        await host.Send("POST", "/account/sessions/revoke-all", 204, bearer: first.AccessToken);
        await host.InvalidAccess(first.AccessToken);
    }

    [Fact]
    public async Task ACC05_Password_change_revokes_every_family()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var first = await host.Login();
        var second = await host.Login();
        await host.Send("POST", "/account/password/change", 401, new ChangePasswordRequest(NewPassword, Password), first.AccessToken, code: "invalid_credentials");
        await host.Send("POST", "/account/password/change", 204, new ChangePasswordRequest(Password, NewPassword), first.AccessToken);
        foreach (var session in new[] { first, second })
        {
            await host.InvalidAccess(session.AccessToken);
            await host.Send("POST", "/auth/refresh", 401, new { refreshToken = session.RefreshToken }, code: "invalid_session");
        }
        await host.Send("POST", "/auth/login", 401, LoginBody(), code: "invalid_credentials");
        var replacement = await host.Login(password: NewPassword);
        await host.Send("POST", "/auth/logout", 204, bearer: replacement.AccessToken);
        await host.InvalidAccess(replacement.AccessToken);
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='password_change'"));
    }

    [Fact]
    public async Task ACC06_Generic_wrong_unknown_and_persisted_locked_login()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        string expected;
        await using (var host = new AccountApiTestHost(db))
        {
            await host.Register();
            expected = (await host.Send("POST", "/auth/login", 401, LoginBody("unknown"), code: "invalid_credentials")).GetRawText();
            for (var i = 0; i < 5; i++)
                Assert.Equal(expected, (await host.Send("POST", "/auth/login", 401, LoginBody(password: NewPassword), code: "invalid_credentials")).GetRawText());
        }
        await using var restarted = new AccountApiTestHost(db);
        Assert.Equal(expected, (await restarted.Send("POST", "/auth/login", 401, LoginBody(), code: "invalid_credentials")).GetRawText());
        Assert.Equal(5, await db.ScalarAsync<int>($"SELECT failed_count FROM {db.QuotedSchema}.password_credentials"));
    }

    [Fact]
    public async Task ACC08_Restart_preserves_live_and_revoked_sessions()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        SessionResponse live, revoked;
        await using (var host = new AccountApiTestHost(db))
        {
            await host.Register(); live = await host.Login(); revoked = await host.Login();
            await host.Send("POST", "/auth/logout", 204, bearer: revoked.AccessToken);
        }
        await using var restarted = new AccountApiTestHost(db);
        await restarted.Send("GET", "/account/me", 200, bearer: live.AccessToken);
        await restarted.InvalidAccess(revoked.AccessToken);
    }
}
