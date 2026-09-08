using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthBindingTests
{
    [Theory]
    [InlineData("revoke")]
    [InlineData("version")]
    [InlineData("expired-proof")]
    [InlineData("reservation")]
    [InlineData("consumed")]
    [InlineData("other-session")]
    public async Task Link_rechecks_live_session_version_and_reserved_proof_at_exchange(string kind)
    {
        await using var h = await Create();
        var session = await AccountTestSupport.Seed(h.Accounts);
        var proof = await h.Service.PasswordProofAsync(session.AccessToken, new(AccountTestSupport.Password, "link:yandex"), Ct);
        var p = await h.Start("link", session.AccessToken, proof.ProofToken);
        await Assert.ThrowsAsync<ExternalAuthException>(() => h.Start("link", session.AccessToken, proof.ProofToken));
        if (kind == "expired-proof") h.Clock.Now = h.Clock.Now.AddMinutes(5);
        var request = await h.Complete(p);
        var s = h.Db.QuotedSchema;
        if (kind == "version") await h.Db.ExecuteAsync($"UPDATE {s}.users SET credential_version=credential_version+1");
        if (kind == "reservation") await h.Db.ExecuteAsync($"UPDATE {s}.reauth_proofs SET reservation='{Guid.NewGuid()}'");
        if (kind == "consumed") await h.Db.ExecuteAsync($"UPDATE {s}.reauth_proofs SET consumed_at=expires_at-interval '1 minute'");
        if (kind == "revoke") await h.Accounts.LogoutAsync(session.AccessToken, Ct);
        var access = kind == "other-session" ? (await h.Accounts.LoginAsync(AccountTestSupport.Login(), Ct)).AccessToken : session.AccessToken;
        var error = await Record.ExceptionAsync(() => h.Service.ExchangeAsync(request, access, Ct));
        Assert.True(error is ExternalAuthException or AccountServiceException);
        Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {s}.external_identities"));
        Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {s}.oauth_transactions WHERE status='completed'"));
    }

    [Fact]
    public async Task Concurrent_cross_user_link_has_only_one_owner()
    {
        await using var h = await Create();
        var a = await AccountTestSupport.Seed(h.Accounts, "user.a");
        var b = await AccountTestSupport.Seed(h.Accounts, "user.b");
        var pa = await h.Service.PasswordProofAsync(a.AccessToken, new(AccountTestSupport.Password, "link:yandex"), Ct);
        var pb = await h.Service.PasswordProofAsync(b.AccessToken, new(AccountTestSupport.Password, "link:yandex"), Ct);
        var ra = await h.Complete(await h.Start("link", a.AccessToken, pa.ProofToken));
        var rb = await h.Complete(await h.Start("link", b.AccessToken, pb.ProofToken));
        async Task<bool> Link(Zapara.Contracts.Accounts.ExternalRequests.ExternalExchangeRequest request, string access)
        { try { await h.Service.ExchangeAsync(request, access, Ct); return true; } catch (ExternalAuthException e) { Assert.Equal(409, e.Status); return false; } }
        var outcomes = await Task.WhenAll(Link(ra, a.AccessToken), Link(rb, b.AccessToken));
        Assert.Single(outcomes, x => x);
        Assert.Equal(1L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.external_identities"));
    }

    [Fact]
    public async Task Reauth_other_identity_never_switches_account_or_issues_proof()
    {
        await using var h = await Create();
        var session = (await h.Flow()).Session!;
        h.Handler.Subject = "not-linked-to-initiator";
        await Assert.ThrowsAsync<ExternalAuthException>(() => h.Flow("reauth", session.AccessToken, scope: "set_password"));
        Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.reauth_proofs"));
        Assert.Equal(1L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.users"));
    }
}
