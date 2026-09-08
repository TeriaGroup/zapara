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
public sealed record PasswordProofRequest(string CurrentPassword, string Purpose)
{
    public override string ToString() => "PasswordProofRequest { [REDACTED] }";
}
public sealed record ProofRequest(string ProofToken)
{
    public override string ToString() => "ProofRequest { [REDACTED] }";
}
public sealed record FirstPasswordRequest(string NewPassword, string ProofToken)
{
    public override string ToString() => "FirstPasswordRequest { [REDACTED] }";
}
