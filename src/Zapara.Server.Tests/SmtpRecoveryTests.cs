using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class SmtpRecoveryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Security", "none")]
    [InlineData("Host", "smtp.invalid\ncommand")]
    [InlineData("From", "a@example.invalid\r\nBcc: victim@example.invalid")]
    [InlineData("Password", "")]
    public void InvalidSmtpMetadataDisablesCapabilityWithoutEchoingSecrets(string key, string value)
    {
        var settings = Settings();
        settings["Accounts:Recovery:Smtp:" + key] = value;
        Assert.Null(SmtpRecoveryConfiguration.Read(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
    }

    [Fact]
    public async Task ConfiguredProductionTransportDeliversSingleUseCodesAndNeutralResetResponse()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        var capture = new CapturingSmtp();
        await using var host = new WebAccountHost(db, environment: "Production", registration: true, moduleSettings: Settings(), configureServices: services =>
        {
            services.RemoveAll<IRecoverySmtpTransport>();
            services.AddSingleton<IRecoverySmtpTransport>(capture);
        });
        var bootstrap = await host.Bootstrap();
        Assert.True(bootstrap.GetProperty("capabilities").GetProperty("recovery").GetBoolean());
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("smtp_user", WebAccountHost.Password));
        await host.Login("smtp_user");
        var proof = await host.Send("POST", "/account/reauthenticate", 200, new PasswordProofRequest(WebAccountHost.Password, "set_recovery_email"));
        await host.Send("POST", "/account/recovery-email/start", 202, new StartRecoveryEmailRequest("owner@example.invalid", proof.GetProperty("proofToken").GetString()!));
        var verify = Assert.Single(capture.Messages);
        var token = verify.Body.Split('\n')[2];
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain(token, verify.ToString());
        await host.Send("POST", "/account/recovery-email/confirm", 204, new ConfirmRecoveryEmailRequest(token));
        await host.Send("POST", "/account/recovery-email/confirm", 400, new ConfirmRecoveryEmailRequest(token));
        var known = await host.Send("POST", "/auth/password-reset/request", 202, new PasswordResetRequest("smtp_user"));
        var unknown = await host.Send("POST", "/auth/password-reset/request", 202, new PasswordResetRequest("unknown_user"));
        Assert.Equal(known.GetRawText(), unknown.GetRawText());
        Assert.Equal(2, capture.Messages.Count);
        Assert.All(capture.Messages, message => Assert.DoesNotContain("smtp_user", message.Subject + message.Body));
    }
    private static Dictionary<string, string?> Settings() => new()
    {
        ["Accounts:Recovery:Smtp:Enabled"] = "true", ["Accounts:Recovery:Smtp:Host"] = "smtp.example.invalid",
        ["Accounts:Recovery:Smtp:Security"] = "starttls", ["Accounts:Recovery:Smtp:From"] = "no-reply@example.invalid",
        ["Accounts:Recovery:Smtp:Username"] = "smtp-user", ["Accounts:Recovery:Smtp:Password"] = "secret-canary"
    };
    private sealed class CapturingSmtp : IRecoverySmtpTransport
    {
        internal List<RecoveryMail> Messages { get; } = [];
        public Task SendAsync(SmtpRecoveryConfiguration configuration, RecoveryMail mail, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Messages.Add(mail); return Task.CompletedTask; }
    }
}
