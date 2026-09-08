using System.Text.Json.Serialization;

namespace Zapara.Contracts.Accounts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest
{
    [JsonConstructor]
    public RegisterRequest(string username, string password, string? displayName = null)
        => (Username, Password, DisplayName) = (AccountValidation.Username(username), AccountValidation.Password(password), AccountValidation.DisplayName(displayName));
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string Password { get; private init; }
    public string? DisplayName { get; }
    public override string ToString() => "RegisterRequest { [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeviceInput
{
    [JsonConstructor]
    public DeviceInput(Guid deviceId, string deviceName, string platform)
        => (DeviceId, DeviceName, Platform) = (AccountValidation.Id(deviceId), AccountValidation.DeviceName(deviceName), AccountValidation.Platform(platform));
    [JsonRequired, JsonInclude] public Guid DeviceId { get; private init; }
    [JsonRequired, JsonInclude] public string DeviceName { get; private init; }
    [JsonRequired, JsonInclude] public string Platform { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginRequest
{
    [JsonConstructor]
    public LoginRequest(string username, string password, DeviceInput device)
        => (Username, Password, Device) = (AccountValidation.Username(username), AccountValidation.Password(password), device ?? throw new ArgumentException("Не задано устройство."));
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string Password { get; private init; }
    [JsonRequired, JsonInclude] public DeviceInput Device { get; private init; }
    public override string ToString() => "LoginRequest { [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefreshRequest
{
    [JsonConstructor]
    public RefreshRequest(string refreshToken) => RefreshToken = AccountValidation.Token(refreshToken, "zr_");
    [JsonRequired, JsonInclude] public string RefreshToken { get; private init; }
    public override string ToString() => "RefreshRequest { [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProfileRequest
{
    [JsonConstructor]
    public UpdateProfileRequest(string? displayName) => DisplayName = AccountValidation.DisplayName(displayName);
    [JsonRequired, JsonInclude] public string? DisplayName { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangePasswordRequest
{
    [JsonConstructor]
    public ChangePasswordRequest(string currentPassword, string newPassword)
        => (CurrentPassword, NewPassword) = (AccountValidation.Password(currentPassword), AccountValidation.Password(newPassword));
    [JsonRequired, JsonInclude] public string CurrentPassword { get; private init; }
    [JsonRequired, JsonInclude] public string NewPassword { get; private init; }
    public override string ToString() => "ChangePasswordRequest { [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartRecoveryEmailRequest
{
    [JsonConstructor]
    public StartRecoveryEmailRequest(string email, string proofToken)
        => (Email, ProofToken) = (AccountValidation.Email(email), proofToken ?? throw new ArgumentException("Недопустимые данные аккаунта."));
    [JsonRequired, JsonInclude] public string Email { get; private init; }
    [JsonRequired, JsonInclude] public string ProofToken { get; private init; }
    public override string ToString() => "StartRecoveryEmailRequest { [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmRecoveryEmailRequest
{
    [JsonConstructor]
    public ConfirmRecoveryEmailRequest(string token)
        => Token = token ?? throw new ArgumentException("Недопустимые данные аккаунта.");
    [JsonRequired, JsonInclude] public string Token { get; private init; }
    public override string ToString() => "ConfirmRecoveryEmailRequest { [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordResetRequest
{
    [JsonConstructor]
    public PasswordResetRequest(string username)
        => Username = username ?? throw new ArgumentException("Недопустимые данные аккаунта.");
    [JsonRequired, JsonInclude] public string Username { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordResetConfirmRequest
{
    [JsonConstructor]
    public PasswordResetConfirmRequest(string token, string newPassword)
        => (Token, NewPassword) = (token ?? throw new ArgumentException("Недопустимые данные аккаунта."), AccountValidation.Password(newPassword));
    [JsonRequired, JsonInclude] public string Token { get; private init; }
    [JsonRequired, JsonInclude] public string NewPassword { get; private init; }
    public override string ToString() => "PasswordResetConfirmRequest { [REDACTED] }";
}
