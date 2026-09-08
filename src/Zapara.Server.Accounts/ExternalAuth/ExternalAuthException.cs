namespace Zapara.Server.Accounts;

public sealed class ExternalAuthException : Exception
{
    internal ExternalAuthException(int status, string code) : base("Не удалось выполнить вход.")
        => (Status, Code) = (status, code);
    public int Status { get; }
    public string Code { get; }
    internal static ExternalAuthException Invalid() => new(403, "invalid_external_proof");
    internal static ExternalAuthException Gone() => new(410, "external_attempt_expired");
    internal static ExternalAuthException Unavailable() => new(503, "provider_unavailable");
}
