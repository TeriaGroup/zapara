using System.Text.Json;
using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.OAuthTestHarness;

namespace Zapara.Server.Tests;

public sealed class OAuthLifecycleTests
{
    [Theory]
    [InlineData(59999, "awaitingApp")]
    [InlineData(60000, "expired")]
    [InlineData(61000, "expired")]
    public async Task Poll_and_exchange_agree_at_handoff_deadline(int elapsedMilliseconds, string expected)
    {
        await using var h = await Create();
        var pending = await h.Start();
        var request = await h.Complete(pending);
        Assert.Equal(540d, await h.Db.ScalarAsync<double>($"SELECT EXTRACT(EPOCH FROM (expires_at-handoff_expires_at))::double precision FROM {h.Db.QuotedSchema}.oauth_transactions"));
        h.Clock.Now = h.Clock.Now.AddMilliseconds(elapsedMilliseconds);
        if (expected == "expired")
        {
            await Assert.ThrowsAsync<ExternalAuthException>(() => h.Service.ExchangeAsync(request, ct: Ct));
            foreach (var table in new[] { "users", "external_identities", "session_families", "access_tokens", "refresh_tokens" })
                Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.{table}"));
        }
        await AssertStatusOnly(h, pending.Start.TransactionId, expected);
        Assert.Equal(2, h.Handler.Count);
        if (expected == "awaitingApp")
        {
            Assert.NotNull((await h.Service.ExchangeAsync(request, ct: Ct)).Session);
            await AssertStatusOnly(h, pending.Start.TransactionId, "completed");
        }
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("callbackClaimed")]
    [InlineData("failed")]
    public async Task Poll_preserves_non_handoff_states_with_null_handoff_deadline(string status)
    {
        await using var h = await Create();
        var pending = await h.Start();
        // Model the persisted provider-progress/failed state without a live provider call.
        await h.Db.ExecuteAsync($"UPDATE {h.Db.QuotedSchema}.oauth_transactions SET status='{status}'");
        h.Clock.Now = h.Clock.Now.AddMilliseconds(59999);
        await AssertStatusOnly(h, pending.Start.TransactionId, status);
        h.Clock.Now = h.Clock.Now.AddMilliseconds(1001);
        await AssertStatusOnly(h, pending.Start.TransactionId, status);
        Assert.Equal(0, h.Handler.Count);
    }

    [Fact]
    public async Task Poll_preserves_completed_after_consumed_handoff_deadline()
    {
        await using var h = await Create();
        var pending = await h.Start();
        var request = await h.Complete(pending);
        Assert.NotNull((await h.Service.ExchangeAsync(request, ct: Ct)).Session);
        h.Clock.Now = h.Clock.Now.AddSeconds(61);
        await AssertStatusOnly(h, pending.Start.TransactionId, "completed");
        await Assert.ThrowsAsync<ExternalAuthException>(() => h.Service.ExchangeAsync(request, ct: Ct));
        Assert.Equal(1L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.session_families"));
    }

    [Fact]
    public async Task Poll_and_cleanup_preserve_ten_minute_transaction_deadline()
    {
        await using var h = await Create();
        var pending = await h.Start();
        h.Clock.Now = h.Clock.Now.AddMinutes(10).AddMilliseconds(-1);
        await AssertStatusOnly(h, pending.Start.TransactionId, "pending");
        h.Clock.Now = h.Clock.Now.AddMilliseconds(1);
        await AssertStatusOnly(h, pending.Start.TransactionId, "expired");
        await h.Service.CleanupAsync(Ct);
        Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.oauth_transactions"));
        await Assert.ThrowsAsync<ExternalAuthException>(() => h.Service.StatusAsync(pending.Start.TransactionId, Ct));
    }

    private static async Task AssertStatusOnly(OAuthTestHarness h, Guid transactionId, string expected)
    {
        var response = await h.Service.StatusAsync(transactionId, Ct);
        var property = Assert.Single(JsonSerializer.SerializeToElement(response).EnumerateObject());
        Assert.Equal("Status", property.Name);
        Assert.Equal(expected, property.Value.GetString());
    }

    [Fact]
    public async Task Poll_contains_only_status_restart_fails_closed_and_expired_rows_are_cleaned()
    {
        await using var h = await Create();
        var pending = await h.Start();
        var error = await Record.ExceptionAsync(async () =>
        {
            var request = await h.Complete(pending);
            var status = await h.Service.StatusAsync(pending.Start.TransactionId, Ct);
            Assert.Equal("awaitingApp", status.Status);
            Assert.Single(JsonSerializer.SerializeToElement(status).EnumerateObject());
            var restarted = new ExternalAuthService(h.Db.DataSource, h.Db.Configuration, h.Clock, h.Registry);
            await Assert.ThrowsAsync<ExternalAuthException>(() => restarted.ExchangeAsync(request, ct: Ct));
            h.Clock.Now = h.Clock.Now.AddSeconds(61);
            await Assert.ThrowsAsync<ExternalAuthException>(() => h.Service.ExchangeAsync(request, ct: Ct));
            h.Clock.Now = h.Clock.Now.AddMinutes(10);
            await restarted.CleanupAsync(Ct);
            Assert.Equal(0L, await h.Db.ScalarAsync<long>($"SELECT count(*) FROM {h.Db.QuotedSchema}.oauth_transactions"));
        });
        Assert.Null(error);
    }

    [Fact]
    public async Task Identity_listing_exposes_provider_and_date_only()
    {
        await using var h = await Create();
        var session = (await h.Flow()).Session!;
        var error = await Record.ExceptionAsync(async () =>
        {
            var identities = await h.Service.IdentitiesAsync(session.AccessToken, Ct);
            Assert.Equal("yandex", Assert.Single(identities).Provider);
            Assert.Equal(2, JsonSerializer.SerializeToElement(identities[0]).EnumerateObject().Count());
        });
        Assert.Null(error);
    }
}
