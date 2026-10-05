using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapara.Contracts.Accounts;

public static class AccountJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

public sealed record UserResponse
{
    [JsonConstructor]
    public UserResponse(Guid userId, string username, string? displayName, DateTimeOffset createdAt)
        => (UserId, Username, DisplayName, CreatedAt) = (AccountValidation.Id(userId), AccountValidation.Username(username), AccountValidation.DisplayName(displayName), AccountValidation.Utc(createdAt));
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string? DisplayName { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
}

public sealed record SessionResponse
{
    [JsonConstructor]
    public SessionResponse(UserResponse user, Guid familyId, string accessToken, string refreshToken,
        string tokenType, DateTimeOffset accessExpiresAt, DateTimeOffset refreshExpiresAt)
    {
        User = user ?? throw new ArgumentException("Не задан пользователь.");
        FamilyId = AccountValidation.Id(familyId);
        AccessToken = AccountValidation.Token(accessToken, "za_");
        RefreshToken = AccountValidation.Token(refreshToken, "zr_");
        TokenType = tokenType == "Bearer" ? tokenType : throw new ArgumentException("Недопустимый тип токена.");
        AccessExpiresAt = AccountValidation.Utc(accessExpiresAt);
        RefreshExpiresAt = AccountValidation.Utc(refreshExpiresAt);
        if (AccessExpiresAt > RefreshExpiresAt) throw new ArgumentException("Недопустимый срок токена.");
    }
    [JsonRequired, JsonInclude] public UserResponse User { get; private init; }
    [JsonRequired, JsonInclude] public Guid FamilyId { get; private init; }
    [JsonRequired, JsonInclude] public string AccessToken { get; private init; }
    [JsonRequired, JsonInclude] public string RefreshToken { get; private init; }
    [JsonRequired, JsonInclude] public string TokenType { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset AccessExpiresAt { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset RefreshExpiresAt { get; private init; }
    public override string ToString() => "SessionResponse { [REDACTED] }";
}

public sealed record MeResponse(UserResponse User, Guid FamilyId, IReadOnlyList<string> AuthenticationMethods);
public sealed record DeviceResponse(Guid FamilyId, Guid DeviceId, string DeviceName, string Platform,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt, bool IsCurrent);
public sealed record DevicesResponse(IReadOnlyList<DeviceResponse> Devices, string? NextCursor);
public sealed record ExportJobResponse(Guid ExportId, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, DateTimeOffset? ExpiresAt);
public sealed record DeleteAccountResponse(string Status, bool RemoteWipe);
public sealed record AccountError(string Title, int Status, string Code);
