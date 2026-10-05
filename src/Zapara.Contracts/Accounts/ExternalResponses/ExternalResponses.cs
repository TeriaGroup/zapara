namespace Zapara.Contracts.Accounts.ExternalResponses;

public sealed record ExternalStartResponse(Guid TransactionId, string AuthorizeUrl, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "ExternalStartResponse { [REDACTED] }";
}
public sealed record ExternalExchangeResponse(string Status, SessionResponse? Session = null, ReauthResponse? Proof = null)
{
    public override string ToString() => "ExternalExchangeResponse { [REDACTED] }";
}
public sealed record ReauthResponse(string ProofToken, string Purpose, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "ReauthResponse { [REDACTED] }";
}
public sealed record ExternalStatusResponse(string Status);
public sealed record ExternalIdentityResponse(string Provider, DateTimeOffset LinkedAt);
public sealed record AuthCapabilitiesResponse(bool Password, bool Vk, bool Yandex, bool Registration, bool Recovery);
