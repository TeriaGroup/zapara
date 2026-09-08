using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthAtomicityTests
{
    [Fact]
    public async Task Native_deadline_is_rechecked_after_waiting_on_identity_owner_lock()
    {
        await using var h = await Create();
        var existing = (await h.Flow()).Session!;
        var request = await h.Complete(await h.Start());
        await using var gate = await AccountDatabaseGate.LockUser(h.Db, existing.User.UserId);
        var exchange = h.Service.ExchangeAsync(request, ct: Ct);
        await AccountDatabaseGate.WaitFor(h.Db, 1);
        h.Clock.Now = h.Clock.Now.AddSeconds(60);
        await gate.Commit();
        var error = await Record.ExceptionAsync(() => exchange);
        Assert.IsType<ExternalAuthException>(error);
        Assert.Equal(1L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.session_families"));
    }

    [Theory]
    [InlineData("users")]
    [InlineData("external_identities")]
    [InlineData("access_tokens")]
    [InlineData("account_security_events")]
    public async Task Failed_native_exchange_rolls_back_account_identity_session_and_consumption(string table)
    {
        await using var h = await Create();
        var request = await h.Complete(await h.Start());
        var s = h.Db.QuotedSchema;
        await h.Db.ExecuteAsync($"""
            CREATE FUNCTION {s}.reject_oauth() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic rejection'; END $$;
            CREATE TRIGGER reject_oauth BEFORE INSERT ON {s}.{table} FOR EACH ROW EXECUTE FUNCTION {s}.reject_oauth()
            """);
        await Assert.ThrowsAsync<AccountServiceException>(() => h.Service.ExchangeAsync(request, ct: Ct));
        foreach (var name in new[] { "users", "external_identities", "session_families", "access_tokens", "refresh_tokens" })
            Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {s}.{name}"));
        Assert.Equal("awaitingApp", (await h.Service.StatusAsync(request.TransactionId, Ct)).Status);
        await h.Db.ExecuteAsync($"DROP TRIGGER reject_oauth ON {s}.{table}");
        Assert.NotNull((await h.Service.ExchangeAsync(request, ct: Ct)).Session);
        Assert.Equal(2, h.Handler.Count);
    }

    [Fact]
    public async Task Revocation_while_link_exchange_waits_on_user_is_rechecked()
    {
        await using var h = await Create();
        var session = await AccountTestSupport.Seed(h.Accounts);
        var proof = await h.Service.PasswordProofAsync(session.AccessToken, new(AccountTestSupport.Password, "link:yandex"), Ct);
        var request = await h.Complete(await h.Start("link", session.AccessToken, proof.ProofToken));
        await using var gate = await AccountDatabaseGate.LockUser(h.Db, session.User.UserId);
        var revoke = h.Accounts.LogoutAsync(session.AccessToken, Ct);
        await AccountDatabaseGate.WaitFor(h.Db, 1);
        var exchange = h.Service.ExchangeAsync(request, session.AccessToken, Ct);
        await AccountDatabaseGate.WaitFor(h.Db, 2);
        await gate.Commit();
        await revoke;
        await Assert.ThrowsAsync<AccountServiceException>(() => exchange);
        Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.external_identities"));
    }
}
