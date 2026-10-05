using System.Net;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class AccountSessionResumptionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AccountServerScope Scope => new(new("https://example.invalid/root"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_v2_refresh_resumes_with_the_same_attempt_after_restart(bool serverUnavailable)
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var requests = new List<(string Path, string Attempt)>();
        using var handler = new AccountClientHandler { Send = async (request, ct) =>
        {
            var attempt = request.Headers.TryGetValues("X-Zapara-Refresh-Attempt", out var values)
                ? values.Single() : "";
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(Session().RefreshToken, body.RootElement.GetProperty("refreshToken").GetString());
            requests.Add((request.RequestUri!.AbsolutePath, attempt));
            if (requests.Count == 1)
            {
                if (serverUnavailable)
                    return Json(new { status = 503, code = "db_unavailable" }, HttpStatusCode.ServiceUnavailable);
                throw new HttpRequestException("synthetic transport failure");
            }
            return Json(Session(2));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());

        var first = new AccountSessionManager(client, vault, new AccountClientClock());
        var failure = await Assert.ThrowsAsync<AccountClientException>(() => first.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(serverUnavailable ? AccountClientFailure.DbUnavailable : AccountClientFailure.Transport, failure.Failure);

        var restarted = new AccountSessionManager(client, vault, new AccountClientClock());
        Assert.Equal(Session(2), await restarted.GetValidSessionAsync(Ct));
        Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, item => Assert.EndsWith("/api/v2/auth/refresh", item.Path));
        Assert.True(Guid.TryParseExact(requests[0].Attempt, "D", out var parsed) && parsed != Guid.Empty);
        Assert.Equal(parsed.ToString("D"), requests[0].Attempt);
        Assert.Equal(requests[0].Attempt, requests[1].Attempt);
    }

    [Fact]
    public async Task V2_404_falls_back_once_to_legacy_and_never_replays_ambiguous_v1_refresh()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var paths = new List<string>();
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (path.EndsWith("/api/v2/auth/refresh", StringComparison.Ordinal))
                return Task.FromResult(Json(new { status = 404, code = "not_found" }, HttpStatusCode.NotFound));
            Assert.Null(vault.Entry!.RefreshAttemptId);
            throw new HttpRequestException("ambiguous legacy request");
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());

        var first = new AccountSessionManager(client, vault, new AccountClientClock());
        var failure = await Assert.ThrowsAsync<AccountClientException>(() => first.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(AccountClientFailure.Transport, failure.Failure);
        Assert.Equal(new[] { "/root/api/v2/auth/refresh", "/root/api/v1/auth/refresh" }, paths);
        Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);

        var restarted = new AccountSessionManager(client, vault, new AccountClientClock());
        var restartFailure = await Assert.ThrowsAsync<AccountClientException>(() => restarted.GetValidSessionAsync(Ct));
        Assert.Equal(AccountClientFailure.ReauthenticationRequired, restartFailure.Failure);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task V2_application_404_does_not_send_refresh_to_legacy_endpoint()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var paths = new List<string>();
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new { status = 404, code = "session_not_found" }, HttpStatusCode.NotFound));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());

        var failure = await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(AccountClientFailure.SessionNotFound, failure.Failure);
        Assert.Equal(new[] { "/root/api/v2/auth/refresh" }, paths);
        Assert.Null(vault.Entry!.RefreshAttemptId);
        var later = await Assert.ThrowsAsync<AccountClientException>(() => manager.GetValidSessionAsync(Ct));
        Assert.Equal(AccountClientFailure.ReauthenticationRequired, later.Failure);
        Assert.Single(paths);
    }

    [Fact]
    public async Task Resumed_replacement_with_expired_access_rotates_again_with_new_attempt()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var attempts = new List<string>();
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            attempts.Add(request.Headers.TryGetValues("X-Zapara-Refresh-Attempt", out var values)
                ? values.Single() : "");
            if (attempts.Count == 1) throw new HttpRequestException("response lost after server rotation");
            if (attempts.Count == 2) return Task.FromResult(Json(Session(2)));
            return Task.FromResult(Json(new Zapara.Contracts.Accounts.SessionResponse(User, FamilyId,
                Token("za_", 3), Token("zr_", 3), "Bearer", Now.AddMinutes(35), Now.AddDays(30))));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var first = new AccountSessionManager(client, vault, new AccountClientClock());
        await Assert.ThrowsAsync<AccountClientException>(() => first.RefreshIfCurrentAsync(Session(), Ct));

        var later = new AccountClientClock { Now = Now.AddMinutes(20) };
        var recovered = await new AccountSessionManager(client, vault, later).GetValidSessionAsync(Ct);
        Assert.Equal(Token("za_", 3), recovered.AccessToken);
        Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
        Assert.Equal(3, attempts.Count);
        Assert.Equal(attempts[0], attempts[1]);
        Assert.NotEqual(attempts[1], attempts[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_or_unsaved_v2_result_recovers_with_its_original_attempt(bool readyWriteFails)
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var attempts = new List<string>();
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            attempts.Add(request.Headers.GetValues("X-Zapara-Refresh-Attempt").Single());
            if (!readyWriteFails && attempts.Count == 1)
            {
                cancel.Cancel();
                throw new OperationCanceledException(cancel.Token);
            }
            return Task.FromResult(Json(Session(2)));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());
        if (readyWriteFails)
        {
            vault.FailReady = true;
            var failure = await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
            Assert.Equal(AccountClientFailure.VaultUnavailable, failure.Failure);
            vault.FailReady = false;
        }
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.RefreshIfCurrentAsync(Session(), cancel.Token));

        var restarted = new AccountSessionManager(client, vault, new AccountClientClock());
        Assert.Equal(Session(2), await restarted.GetValidSessionAsync(Ct));
        Assert.Equal(attempts[0], attempts[1]);
        Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
    }

    [Fact]
    public async Task V2_404_with_successful_legacy_refresh_publishes_ready_session()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var calls = 0;
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.EndsWith("/api/v2/auth/refresh", request.RequestUri!.AbsolutePath);
                return Task.FromResult(Json(new { status = 404, code = "not_found" }, HttpStatusCode.NotFound));
            }
            Assert.EndsWith("/api/v1/auth/refresh", request.RequestUri!.AbsolutePath);
            Assert.False(request.Headers.Contains("X-Zapara-Refresh-Attempt"));
            return Task.FromResult(Json(Session(2)));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());

        Assert.Equal(Session(2), await manager.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
        Assert.Equal(Session(2), await manager.GetValidSessionAsync(Ct));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(-11, false)]
    [InlineData(1, false)]
    public async Task Refresh_accepts_at_most_one_microsecond_of_family_expiry_precision_loss(long deltaTicks, bool accepted)
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var replacement = Session(2);
        replacement = new(replacement.User, replacement.FamilyId, replacement.AccessToken, replacement.RefreshToken,
            "Bearer", replacement.AccessExpiresAt, replacement.RefreshExpiresAt.AddTicks(deltaTicks));
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Json(replacement)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());

        if (accepted)
        {
            Assert.Equal(replacement, await manager.RefreshIfCurrentAsync(Session(), Ct));
            Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
        }
        else
        {
            var error = await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
            Assert.Equal(AccountClientFailure.InvalidPayload, error.Failure);
            Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);
        }
    }
}
