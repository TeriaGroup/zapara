namespace Zapara.Server.Accounts.ExternalProviders;

public enum ExternalProviderFailure
{
    NotConfigured, InvalidConfiguration, InvalidRequest, ProviderRejected,
    InvalidResponse, IdentityMismatch, TransportFailure, Timeout
}

public sealed class ExternalProviderException : Exception
{
    internal ExternalProviderException(ExternalProviderFailure failure)
        : base("External provider operation failed: " + failure) => Failure = failure;
    public ExternalProviderFailure Failure { get; }
}

public sealed class VerifiedExternalIdentity
{
    internal VerifiedExternalIdentity(string provider, string subject, string? displayName)
        => (Provider, Subject, DisplayName) = (provider, subject, displayName);
    public string Provider { get; }
    public string Subject { get; }
    public string? DisplayName { get; }
    public override string ToString() => nameof(VerifiedExternalIdentity);
}

// Only the coordinator can establish ownership, entropy, correlation and one-use semantics.
// These wrappers validate format, NOT the origin or authenticity of caller-supplied values.
public sealed class ProviderState
{
    public ProviderState(string value) => Value = ProviderValidation.UrlSafe(value, 32, 1024);
    internal string Value { get; }
    public override string ToString() => "ProviderState [redacted]";
}

public sealed class CodeVerifier
{
    public CodeVerifier(string value) => Value = ProviderValidation.UrlSafe(value, 43, 128);
    internal string Value { get; }
    public override string ToString() => "CodeVerifier [redacted]";
}

public sealed class CodeChallenge
{
    public CodeChallenge(string value)
    {
        Value = ProviderValidation.UrlSafe(value, 43, 43);
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
        if (bytes.Length != 32 || Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value)
            throw new ExternalProviderException(ExternalProviderFailure.InvalidRequest);
    }
    internal string Value { get; }
    public override string ToString() => "CodeChallenge [redacted]";
}

public interface IExternalProviderAdapter : IDisposable
{
    string Provider { get; }
    bool IsConfigured { get; }
    Uri BuildAuthorizationUri(ProviderState state, CodeChallenge challenge);
    Task<VerifiedExternalIdentity> ExchangeIdentityAsync(string code, ProviderState state,
        CodeVerifier verifier, string? deviceId = null, CancellationToken ct = default);
}
