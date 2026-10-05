using System.Text.Json.Serialization;
using Zapara.Contracts.Accounts;

namespace Zapara.Contracts.Accounts.ExternalRequests;

public sealed record NativeReturn(string Kind, int? Port = null);
public sealed record ExternalStartRequest(string Purpose, string NativeChallenge, string NativeChallengeMethod,
    DeviceInput Device, NativeReturn NativeReturn, string? ProofToken = null, string? ProofPurpose = null)
{
    public override string ToString() => "ExternalStartRequest { [REDACTED] }";
}
public sealed record ExternalExchangeRequest(Guid TransactionId, string NativeVerifier, string HandoffCode)
{
    public override string ToString() => "ExternalExchangeRequest { [REDACTED] }";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordProofRequest
{
    [JsonConstructor]
    public PasswordProofRequest(string currentPassword, string purpose)
        => (CurrentPassword, Purpose) = (AccountValidation.Password(currentPassword), purpose ?? throw new ArgumentException("Недопустимые данные аккаунта."));
    [JsonRequired, JsonInclude] public string CurrentPassword { get; private init; }
    [JsonRequired, JsonInclude] public string Purpose { get; private init; }
    public override string ToString() => "PasswordProofRequest { [REDACTED] }";
}
public sealed record ProofRequest(string ProofToken)
{
    public override string ToString() => "ProofRequest { [REDACTED] }";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FirstPasswordRequest
{
    [JsonConstructor]
    public FirstPasswordRequest(string newPassword, string proofToken)
        => (NewPassword, ProofToken) = (AccountValidation.Password(newPassword), proofToken ?? throw new ArgumentException("Недопустимые данные аккаунта."));
    [JsonRequired, JsonInclude] public string NewPassword { get; private init; }
    [JsonRequired, JsonInclude] public string ProofToken { get; private init; }
    public override string ToString() => "FirstPasswordRequest { [REDACTED] }";
}
