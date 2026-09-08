using System.Text.Json;
using Xunit;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Tests;

public sealed class AccountContractTests
{
    private static readonly JsonSerializerOptions Json = AccountJson.CreateOptions();

    [Theory]
    [InlineData(".Ab", ".ab")]
    [InlineData("_A9", "_a9")]
    [InlineData("-AA", "-aa")]
    [InlineData("123", "123")]
    public void UsernameCanonical(string input, string canonical) => Assert.Equal(canonical, AccountValidation.NormalizeUsername(input));

    [Theory]
    [InlineData("ab")]
    [InlineData(" abc")]
    [InlineData("abc\n")]
    [InlineData("абв")]
    [InlineData("a@b")]
    public void UsernameRejects(string input) => Assert.Throws<ArgumentException>(() => AccountValidation.Username(input));

    [Fact]
    public void ScalarPasswordPolicyAndBounds()
    {
        Assert.Equal(new string('a', 32), AccountValidation.Username(new string('a', 32)));
        Assert.Throws<ArgumentException>(() => AccountValidation.Username(new string('a', 33)));
        foreach (var count in new[] { 12, 128 })
        {
            var value = string.Concat(Enumerable.Repeat("😀", count));
            Assert.Equal(value, AccountValidation.Password(value));
        }
        Assert.Equal("            ", AccountValidation.Password("            "));
        foreach (var value in new[] { new string('x', 11), new string('x', 129), "12345678901\0", "12345678901\ud800" })
            Assert.Throws<ArgumentException>(() => AccountValidation.Password(value));
        Assert.Throws<ArgumentException>(() => AccountValidation.DisplayName("name\n"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"username\":\"abc\"}")]
    [InlineData("{\"username\":\"abc\",\"password\":\"123456789012\",\"admin\":true}")]
    [InlineData("{\"username\":null,\"password\":\"123456789012\"}")]
    public void InvalidRegisterRejected(string value) => Assert.NotNull(Record.Exception(() => JsonSerializer.Deserialize<RegisterRequest>(value, Json)));

    [Fact]
    public void MissingNullableFieldAndNestedUnknownAreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateProfileRequest>("{}", Json));
        Assert.Null(JsonSerializer.Deserialize<UpdateProfileRequest>("{\"displayName\":null}", Json)!.DisplayName);
        Assert.NotNull(Record.Exception(() => JsonSerializer.Deserialize<LoginRequest>("{\"username\":\"abc\",\"password\":\"123456789012\"}", Json)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DeviceInput>("{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"deviceName\":\"test\",\"platform\":\"windows\",\"trusted\":true}", Json));
    }

    [Fact]
    public void FrozenFixturesRoundTripAndSecretsAreRedacted()
    {
        RoundTrip<RegisterRequest>("register.json");
        RoundTrip<LoginRequest>("login.json");
        RoundTrip<UserResponse>("user.json");
        var session = RoundTrip<SessionResponse>("session.json");
        RoundTrip<AccountError>("error.json");
        var secret = "SECRET_canary_12";
        foreach (var value in new object[] { new RegisterRequest("abc", secret),
            new LoginRequest("abc", secret, new DeviceInput(Guid.NewGuid(), "test", "android")),
            new ChangePasswordRequest(secret, secret), new RefreshRequest(session.RefreshToken), session })
        {
            Assert.Contains("REDACTED", value.ToString());
            Assert.DoesNotContain(secret, value.ToString());
            Assert.DoesNotContain(session.RefreshToken, value.ToString());
            Assert.DoesNotContain(session.AccessToken, value.ToString());
        }
    }

    [Fact]
    public void DeviceTokenAndUtcBoundaries()
    {
        Assert.Throws<ArgumentException>(() => new DeviceInput(Guid.Empty, "test", "windows"));
        Assert.Throws<ArgumentException>(() => new DeviceInput(Guid.NewGuid(), new string('a', 81), "windows"));
        Assert.Throws<ArgumentException>(() => new DeviceInput(Guid.NewGuid(), "test", "ios"));
        Assert.Throws<ArgumentException>(() => new RefreshRequest("zr_" + new string('A', 42) + "B"));
        Assert.Throws<ArgumentException>(() => new RefreshRequest("za_" + new string('A', 43)));
        Assert.Throws<ArgumentException>(() => new UserResponse(Guid.NewGuid(), "abc", null, DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(3))));
    }

    private static T RoundTrip<T>(string file)
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "accounts", file)).Trim();
        var result = JsonSerializer.Deserialize<T>(fixture, Json)!;
        Assert.Equal(fixture, JsonSerializer.Serialize(result, Json));
        return result;
    }
}
