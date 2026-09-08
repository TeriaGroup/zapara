using System.Text.Json;
using Xunit;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Tests;

public sealed class RecoveryContractTests
{
    private static readonly JsonSerializerOptions Json = AccountJson.CreateOptions();

    [Theory]
    [InlineData("user@example.invalid", "user@example.invalid")]
    [InlineData("User.Name+tag@Mail.Example.INVALID", "user.name+tag@mail.example.invalid")]
    public void Email_is_lowercased_without_trimming(string input, string expected)
        => Assert.Equal(expected, AccountValidation.Email(input));

    [Theory]
    [InlineData(" user@example.invalid")]
    [InlineData("user@example.invalid ")]
    [InlineData("user@invalid")]
    [InlineData("not-an-email")]
    [InlineData("user@ex ample.invalid")]
    public void Email_rejects_noncanonical_values(string input)
        => Assert.Throws<ArgumentException>(() => AccountValidation.Email(input));

    [Fact]
    public void Recovery_requests_redact_secrets_and_reject_unknown_members()
    {
        const string secret = "SECRET_canary_12";
        const string email = "w2-recovery-canary@example.invalid";
        var start = new StartRecoveryEmailRequest(email, secret);
        var confirm = new ConfirmRecoveryEmailRequest(secret);
        var reset = new PasswordResetConfirmRequest(secret, secret);
        foreach (var value in new object[] { start, confirm, reset })
        {
            Assert.Contains("REDACTED", value.ToString());
            Assert.DoesNotContain(secret, value.ToString());
            Assert.DoesNotContain(email, value.ToString());
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StartRecoveryEmailRequest>(
            "{\"email\":\"a@b.cd\",\"proofToken\":\"x\",\"extra\":1}", Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PasswordResetRequest>(
            "{\"username\":\"synthetic\",\"extra\":1}", Json));
    }
}
