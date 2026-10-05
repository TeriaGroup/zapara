using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthLinkTests
{
    [Fact]
    public async Task Password_proof_reserves_link_and_provider_reauth_can_unlink_with_password_remaining()
    {
        await using var h = await Create();
        var login = await AccountTestSupport.Seed(h.Accounts);
        var error = await Record.ExceptionAsync(async () =>
        {
            var proof = await h.Service.PasswordProofAsync(login.AccessToken, new(AccountTestSupport.Password, "link:yandex"), Ct);
            var link = await h.Flow("link", login.AccessToken, proof.ProofToken);
            Assert.Null(link.Session);
            Assert.Equal(1L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.external_identities"));
            var again = await h.Flow();
            Assert.Equal(login.User.UserId, again.Session!.User.UserId);
            var reauth = await h.Flow("reauth", login.AccessToken, scope: "unlink:yandex");
            await h.Service.UnlinkAsync(login.AccessToken, "yandex", reauth.Proof!.ProofToken, Ct);
            Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.external_identities"));
        });
        Assert.Null(error);
    }

    [Fact]
    public async Task External_first_user_cannot_unlink_last_method_but_can_set_first_password_using_provider_proof()
    {
        await using var h = await Create();
        var login = (await h.Flow()).Session!;
        var error = await Record.ExceptionAsync(async () =>
        {
            var me = await h.Accounts.GetMeAsync(login.AccessToken, Ct);
            Assert.Equal(new[] { "yandex" }, me.AuthenticationMethods);
            var reauth = await h.Flow("reauth", login.AccessToken, scope: "unlink:yandex");
            var last = await Assert.ThrowsAsync<ExternalAuthException>(() => h.Service.UnlinkAsync(login.AccessToken, "yandex", reauth.Proof!.ProofToken, Ct));
            Assert.Equal(409, last.Status);
            var set = await h.Flow("reauth", login.AccessToken, scope: "set_password");
            await h.Service.SetFirstPasswordAsync(login.AccessToken, new("first valid password 123", set.Proof!.ProofToken), Ct);
            var passwordLogin = await h.Accounts.LoginAsync(new(login.User.Username, "first valid password 123", new(Guid.NewGuid(), "Тест", "windows")), Ct);
            Assert.Equal(login.User.UserId, passwordLogin.User.UserId);
            await Assert.ThrowsAsync<AccountServiceException>(() => h.Accounts.AuthenticateAsync(login.AccessToken, Ct));
        });
        Assert.Null(error);
    }
}
