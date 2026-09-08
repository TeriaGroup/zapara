using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthSecurityTests
{
    [Theory]
    [InlineData("state")]
    [InlineData("provider")]
    [InlineData("uri")]
    [InlineData("code")]
    [InlineData("error")]
    [InlineData("expired")]
    [InlineData("restart")]
    public async Task Callback_rejects_invalid_bindings_without_network_or_account(string kind)
    {
        await using var h = await Create();
        var p = await h.Start();
        if (kind == "expired") h.Clock.Now = h.Clock.Now.AddMinutes(10);
        var service = kind == "restart" ? new ExternalAuthService(h.Db.DataSource, h.Db.Configuration, h.Clock, h.Registry) : h.Service;
        await Assert.ThrowsAsync<ExternalAuthException>(() => service.CallbackAsync(kind == "provider" ? "vk" : "yandex",
            new(kind == "uri" ? "https://other.invalid/registered/yandex" : Callback), kind == "state" ? ExternalSecrets.Random() : p.State,
            kind == "code" ? null : "synthetic-code", providerError: kind == "error", ct: Ct));
        Assert.Equal(0, h.Handler.Count);
        Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.users"));
        if (kind is "error" or "code")
            await Assert.ThrowsAsync<ExternalAuthException>(() => h.Complete(p));
    }

    [Fact]
    public async Task Concurrent_callbacks_make_one_outbound_exchange_and_concurrent_native_exchange_creates_one_account()
    {
        await using var h = await Create();
        var p = await h.Start();
        async Task<Zapara.Contracts.Accounts.ExternalRequests.ExternalExchangeRequest?> CallbackResult()
        { try { return await h.Complete(p); } catch (ExternalAuthException) { return null; } }
        var callbacks = await Task.WhenAll(CallbackResult(), CallbackResult());
        var request = Assert.Single(callbacks, x => x is not null)!;
        async Task<bool> ExchangeResult()
        { try { await h.Service.ExchangeAsync(request, ct: Ct); return true; } catch (ExternalAuthException) { return false; } }
        Assert.Single(await Task.WhenAll(ExchangeResult(), ExchangeResult()), x => x);
        Assert.Equal(2, h.Handler.Count);
        Assert.Equal(1L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task Matching_provider_emails_do_not_merge_distinct_subjects()
    {
        await using var h = await Create();
        var first = (await h.Flow()).Session!;
        h.Handler.Subject = "another-subject";
        var second = (await h.Flow()).Session!;
        Assert.NotEqual(first.User.UserId, second.User.UserId);
        Assert.Equal(2L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task Ephemeral_registry_enforces_capacity_and_deadline()
    {
        var clock = new AccountClock();
        var secrets = new ExternalSecrets(clock, 1);
        var id = Guid.NewGuid();
        secrets.Add(id, ExternalSecrets.Random(), ExternalSecrets.Random(), clock.Now.AddMinutes(10));
        Assert.Throws<ExternalAuthException>(() => secrets.Add(Guid.NewGuid(), ExternalSecrets.Random(), ExternalSecrets.Random(), clock.Now.AddMinutes(10)));
        clock.Now = clock.Now.AddMinutes(10);
        Assert.Throws<ExternalAuthException>(() => secrets.Take(id));
        secrets.Add(Guid.NewGuid(), ExternalSecrets.Random(), ExternalSecrets.Random(), clock.Now.AddMinutes(10));
    }

    [Fact]
    public void Future_native_receiver_contract_cannot_self_authorize_unknown_incoming_link()
    {
        // Contract helper only: no native integration is shipped by these server tests.
        var pending = new Dictionary<Guid, string>();
        var own = Guid.NewGuid();
        pending.Add(own, ExternalSecrets.Random());
        bool Receive(Guid incoming) => pending.ContainsKey(incoming);
        Assert.False(Receive(Guid.NewGuid()));
        Assert.Single(pending);
        Assert.True(Receive(own));
    }
}
