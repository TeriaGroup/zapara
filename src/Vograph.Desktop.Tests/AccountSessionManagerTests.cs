using System.Net;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountSessionManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AccountServerScope Scope => new(new("https://example.invalid/root"));

    [Theory]
    [InlineData("offline")]
    [InlineData("cancel")]
    [InlineData("parse")]
    [InlineData("save")]
    [InlineData("user")]
    [InlineData("family")]
    [InlineData("expiry")]
    [InlineData("unchanged")]
    public async Task Ambiguous_or_invalid_refresh_keeps_pending_and_restart_never_retries(string failure)
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);
            if (failure == "offline") throw new HttpRequestException(Password);
            if (failure == "cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            if (failure == "parse") return Task.FromResult(Raw("{}"));
            if (failure == "save") vault.FailReady = true;
            var next = Session(2, failure == "family" ? Guid.NewGuid() : null, failure == "user" ? Guid.NewGuid() : null);
            if (failure == "expiry") next = new(next.User, next.FamilyId, next.AccessToken, next.RefreshToken,
                "Bearer", next.AccessExpiresAt, next.RefreshExpiresAt.AddDays(1));
            return Task.FromResult(Json(failure == "unchanged" ? Session() : next));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.RefreshIfCurrentAsync(Session(), cancel.Token));
        else await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);
        Assert.Equal(Session(), vault.Entry.Session);
        var restarted = new AccountSessionManager(client, vault, new AccountClientClock());
        var error = await Assert.ThrowsAsync<AccountClientException>(() => restarted.GetValidSessionAsync(Ct));
        Assert.Equal(AccountClientFailure.ReauthenticationRequired, error.Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Two_managers_serialize_and_waiter_reloads_rotated_access()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new AccountClientHandler { Send = async (_, ct) =>
        {
            Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return Json(Session(2));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var first = new AccountSessionManager(client, vault, new AccountClientClock()).RefreshIfCurrentAsync(Session(), Ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var second = new AccountSessionManager(client, vault, new AccountClientClock()).RefreshIfCurrentAsync(Session(), Ct);
        release.SetResult();
        Assert.Equal(Session(2), await first);
        Assert.Equal(Session(2), await second);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
    }

    [Fact]
    public async Task Pending_save_failure_prevents_network_and_preserves_ready()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()), FailPending = true };
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());
        await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(AccountRefreshState.Ready, vault.Entry!.RefreshState);
    }

    [Fact]
    public async Task Offline_logout_clears_locally_first_without_claiming_remote_revocation()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            Assert.Null(vault.Entry);
            throw new HttpRequestException(Password);
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var result = await new AccountSessionManager(client, vault).LogoutAsync(AccountSessionIdentity.From(Session()), Ct);
        Assert.True(result.LocalCleared);
        Assert.False(result.RemoteRevoked);
        Assert.Null(vault.Entry);
    }

    [Fact]
    public async Task Late_A_logout_refresh_and_activation_cannot_clear_or_overwrite_B()
    {
        var b = Session(2, Guid.NewGuid(), Guid.NewGuid());
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, b) };
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var manager = new AccountSessionManager(client, vault);
        Assert.False((await manager.LogoutAsync(AccountSessionIdentity.From(Session()), Ct)).LocalCleared);
        await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
        await Assert.ThrowsAsync<AccountClientException>(() => manager.ActivateAsync(Session(), AccountSessionIdentity.From(Session()), Ct));
        Assert.Equal(b, vault.Entry!.Session);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Successful_password_change_clears_only_credentials()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        await new AccountSessionManager(client, vault).ChangePasswordAsync(AccountSessionIdentity.From(Session()), new(Password, Password + "new"), Ct);
        Assert.Null(vault.Entry);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Login_atomically_saves_ready_and_valid_session_does_not_refresh()
    {
        using var vault = new AccountMemoryVault(Scope.Key);
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri, new AccountClientClock());
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());
        Assert.Equal(Session(), await manager.LoginAsync(Login, Ct));
        Assert.Equal(Session(), await manager.GetValidSessionAsync(Ct));
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(Password, vault.Entry!.ToString());
        Assert.DoesNotContain(Session().AccessToken, vault.Entry.ToString());
    }
}
