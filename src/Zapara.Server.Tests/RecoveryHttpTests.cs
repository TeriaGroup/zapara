using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed class RecoveryHttpTests(ITestOutputHelper output)
{
    private const string Email = "w2-recovery-canary@example.invalid";

    [Fact]
    public async Task Testing_capabilities_enable_recovery_only_with_the_sink()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        var caps = await host.Send("GET", "/auth/capabilities", 200);
        Assert.True(caps.GetProperty("recovery").GetBoolean());
        Assert.True(caps.GetProperty("registration").GetBoolean());
        Assert.IsType<TestingRecoverySink>(host.Factory.Services.GetRequiredService<IRecoveryDelivery>());
    }

    [Fact]
    public async Task Start_without_proof_follows_existing_reauth_contract()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var session = await host.Login();
        await host.Send("POST", "/account/recovery-email/start", 401,
            new StartRecoveryEmailRequest(Email, new string('A', 43)), code: "invalid_session");
        await host.Send("POST", "/account/recovery-email/start", 400,
            new { email = Email }, bearer: session.AccessToken, code: "invalid_request");
        await host.Send("POST", "/account/recovery-email/start", 403,
            new StartRecoveryEmailRequest(Email, new string('A', 43)), session.AccessToken, code: "invalid_external_proof");
    }

    [Fact]
    public async Task Confirm_token_is_single_use_and_does_not_replace_verified_address_on_start()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var session = await host.Login();
        var sink = Sink(host);
        await StartVerify(host, session, Email);
        var first = sink.Last("verify").Token;
        await host.Send("POST", "/account/recovery-email/confirm", 204, new ConfirmRecoveryEmailRequest(first));
        await host.Send("POST", "/account/recovery-email/confirm", 400, new ConfirmRecoveryEmailRequest(first), code: "invalid_request");
        const string pending = "pending.canary@example.invalid";
        await StartVerify(host, session, pending);
        Assert.Equal(Email, await db.ScalarAsync<string>($"SELECT email FROM {db.QuotedSchema}.recovery_addresses"));
        Assert.Equal(pending, sink.Last("verify").Email);
    }

    [Fact]
    public async Task Reset_request_is_identical_for_unknown_and_known_usernames()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var unknown = await host.Send("POST", "/auth/password-reset/request", 202, new PasswordResetRequest("no_such_user"));
        var known = await host.Send("POST", "/auth/password-reset/request", 202, new PasswordResetRequest("synthetic"));
        Assert.Equal(unknown.GetRawText(), known.GetRawText());
        Assert.Equal(JsonValueKind.Object, unknown.ValueKind);
        Assert.Empty(unknown.EnumerateObject());
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.password_reset_tokens"));
    }

    [Fact]
    public async Task Reset_confirm_revokes_all_families_audits_without_secrets_and_does_not_auto_login()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var first = await host.Login();
        var second = await host.Login();
        var sink = Sink(host);
        await StartVerify(host, first, Email);
        await host.Send("POST", "/account/recovery-email/confirm", 204, new ConfirmRecoveryEmailRequest(sink.Last("verify").Token));
        var accepted = await host.Send("POST", "/auth/password-reset/request", 202, new PasswordResetRequest("synthetic"));
        Assert.Empty(accepted.EnumerateObject());
        var token = sink.Last("reset").Token;
        var confirm = await host.Send("POST", "/auth/password-reset/confirm", 204, new PasswordResetConfirmRequest(token, NewPassword));
        Assert.Equal(default, confirm);
        foreach (var session in new[] { first, second })
        {
            await host.InvalidAccess(session.AccessToken);
            await host.Send("POST", "/auth/refresh", 401, new { refreshToken = session.RefreshToken }, code: "invalid_session");
        }
        await host.Send("POST", "/auth/login", 401, LoginBody(), code: "invalid_credentials");
        var replacement = await host.Login(password: NewPassword);
        Assert.NotEqual(first.AccessToken, replacement.AccessToken);
        await host.Send("POST", "/auth/password-reset/confirm", 400, new PasswordResetConfirmRequest(token, NewPassword), code: "invalid_request");
        var audit = await db.ScalarAsync<string>($"SELECT row_to_json(e)::text FROM {db.QuotedSchema}.account_security_events e WHERE action='password_reset'");
        Assert.DoesNotContain(Email, audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(token, audit, StringComparison.Ordinal);
        var logs = string.Join('\n', host.Logs);
        foreach (var secret in new[] { Password, NewPassword, Email, token, first.AccessToken, replacement.AccessToken,
                     Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")! })
            Assert.True(!logs.Contains(secret, StringComparison.Ordinal), "Recovery log canary must be absent.");
        Assert.DoesNotContain(Email, sink.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, sink.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_host_reports_recovery_false_and_start_is_503_without_sending()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using (var local = new AccountApiTestHost(db)) await local.Register();
        await using var production = new AccountApiTestHost(db, "Production");
        var caps = await production.Send("GET", "/auth/capabilities", 200);
        Assert.False(caps.GetProperty("recovery").GetBoolean());
        Assert.False(caps.GetProperty("registration").GetBoolean());
        var session = await production.Login();
        await production.Send("POST", "/account/recovery-email/start", 503,
            new StartRecoveryEmailRequest(Email, new string('A', 43)), session.AccessToken, code: "recovery_unavailable");
        Assert.Null(production.Factory.Services.GetService<IRecoveryDelivery>() as TestingRecoverySink);
        Assert.Null(production.Factory.Services.GetService<TestingRecoverySink>());
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.recovery_email_tokens"));
        var logs = string.Join('\n', production.Logs);
        Assert.DoesNotContain(Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")!, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_request_stays_202_when_delivery_throws_and_does_not_log_secrets()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var prepared = new AccountApiTestHost(db);
        await prepared.Register();
        var session = await prepared.Login();
        await StartVerify(prepared, session, Email);
        await prepared.Send("POST", "/account/recovery-email/confirm", 204,
            new ConfirmRecoveryEmailRequest(Sink(prepared).Last("verify").Token));
        var unknown = await prepared.Send("POST", "/auth/password-reset/request", 202, new PasswordResetRequest("no_such_user"));
        await using var factory = prepared.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRecoveryDelivery>();
            services.AddSingleton<IRecoveryDelivery, ThrowingResetDelivery>();
            services.RemoveAll<RecoveryService>();
            services.AddSingleton<RecoveryService>();
        }));
        using var client = factory.CreateClient();
        using var knownResponse = await client.PostAsJsonAsync("/api/v1/auth/password-reset/request",
            new PasswordResetRequest("synthetic"), Json, Ct);
        Assert.Equal(202, (int)knownResponse.StatusCode);
        var knownText = await knownResponse.Content.ReadAsStringAsync(Ct);
        Assert.Equal(unknown.GetRawText(), knownText);
        var logs = string.Join('\n', prepared.Logs);
        foreach (var secret in new[] { Email, Password, "smtp-canary",
                     Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")! })
            Assert.True(!logs.Contains(secret, StringComparison.Ordinal), "Reset delivery failure must not log secrets.");
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.password_reset_tokens"));
    }

    private static TestingRecoverySink Sink(AccountApiTestHost host)
        => Assert.IsType<TestingRecoverySink>(host.Factory.Services.GetRequiredService<IRecoveryDelivery>());

    private sealed class ThrowingResetDelivery : IRecoveryDelivery
    {
        public Task SendVerificationAsync(Guid userId, string email, string token, CancellationToken ct)
            => Task.CompletedTask;
        public Task SendResetAsync(string email, string token, CancellationToken ct)
            => throw new InvalidOperationException("smtp-canary");
    }

    private static async Task StartVerify(AccountApiTestHost host, SessionResponse session, string email)
    {
        var proof = await host.Send("POST", "/account/reauthenticate", 200,
            new PasswordProofRequest(Password, "set_recovery_email"), session.AccessToken);
        await host.Send("POST", "/account/recovery-email/start", 202,
            new StartRecoveryEmailRequest(email, proof.GetProperty("proofToken").GetString()!), session.AccessToken);
    }
}
