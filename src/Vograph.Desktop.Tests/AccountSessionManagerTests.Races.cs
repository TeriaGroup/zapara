using System.Net;
using Vograph.Core.Services.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountSessionManagerTests
{
    [Fact]
    public async Task Late_login_response_cannot_replace_newly_active_account()
    {
        using var vault = new AccountMemoryVault(Scope.Key);
        var b = Session(2, Guid.NewGuid(), Guid.NewGuid());
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            vault.Entry = AccountVaultEntry.Ready(Scope.Key, b);
            return Task.FromResult(Json(Session()));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var manager = new AccountSessionManager(client, vault);
        Assert.Equal(AccountClientFailure.SessionChanged, (await Assert.ThrowsAsync<AccountClientException>(
            () => manager.LoginAsync(Login, Ct))).Failure);
        Assert.Equal(b, vault.Entry!.Session);
    }

    [Fact]
    public async Task Remote_logout_finishing_after_B_activation_does_not_delete_B()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        var b = Session(2, Guid.NewGuid(), Guid.NewGuid());
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            Assert.Null(vault.Entry);
            vault.Entry = AccountVaultEntry.Ready(Scope.Key, b);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var result = await new AccountSessionManager(client, vault).LogoutAsync(AccountSessionIdentity.From(Session()), Ct);
        Assert.True(result.RemoteRevoked);
        Assert.Equal(b, vault.Entry!.Session);
    }

    [Fact]
    public async Task Clock_skew_get_valid_makes_only_one_attempt_even_when_new_access_appears_expired()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Json(Session(2))) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var clock = new AccountClientClock { Now = Now.AddHours(1) };
        Assert.Equal(Session(2), await new AccountSessionManager(client, vault, clock).GetValidSessionAsync(Ct));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(401, "invalid_session")]
    [InlineData(503, "db_unavailable")]
    public async Task Explicit_refresh_failure_is_not_retried_or_restored_to_READY(int status, string code)
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Json(new { title = Password, status, code }, (HttpStatusCode)status)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());
        await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
        await Assert.ThrowsAsync<AccountClientException>(() => manager.RefreshIfCurrentAsync(Session(), Ct));
        Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Interrupted_response_disposes_stream_and_preserves_pending()
    {
        using var vault = new AccountMemoryVault(Scope.Key) { Entry = AccountVaultEntry.Ready(Scope.Key, Session()) };
        using var stream = new AccountInterruptedStream();
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(stream) }) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, Scope.BaseUri);
        var manager = new AccountSessionManager(client, vault, new AccountClientClock());
        Assert.Equal(AccountClientFailure.Transport, (await Assert.ThrowsAsync<AccountClientException>(
            () => manager.RefreshIfCurrentAsync(Session(), Ct))).Failure);
        Assert.True(stream.Disposed);
        Assert.Equal(AccountRefreshState.Pending, vault.Entry!.RefreshState);
    }
}

internal sealed class AccountInterruptedStream : MemoryStream
{
    internal bool Disposed;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new IOException(AccountClientTestSupport.Password);
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}
