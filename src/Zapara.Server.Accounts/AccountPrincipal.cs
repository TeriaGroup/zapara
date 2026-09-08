namespace Zapara.Server.Accounts;

internal sealed record AccountPrincipal(Guid UserId, Guid FamilyId, long CredentialVersion, ReadOnlyMemory<byte> AccessHash)
{
    public override string ToString() => "AccountPrincipal { [REDACTED] }";
}
