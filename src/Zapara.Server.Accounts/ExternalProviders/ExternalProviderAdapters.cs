namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class VkIdAdapter : IExternalProviderAdapter
{
    private readonly ProviderAdapterCore core;
    public VkIdAdapter(VkIdOptions options, HttpClient? httpClient = null, TimeProvider? timeProvider = null)
        => core = new(true, options?.Metadata ?? throw new ExternalProviderException(ExternalProviderFailure.InvalidConfiguration), httpClient, timeProvider);
    public string Provider => core.Provider;
    public bool IsConfigured => core.IsConfigured;
    public Uri BuildAuthorizationUri(ProviderState state, CodeChallenge challenge) => core.BuildAuthorizationUri(state, challenge);
    public Task<VerifiedExternalIdentity> ExchangeIdentityAsync(string code, ProviderState state,
        CodeVerifier verifier, string? deviceId = null, CancellationToken ct = default)
        => core.ExchangeIdentityAsync(code, state, verifier, deviceId, ct);
    public void Dispose() => core.Dispose();
}

public sealed class YandexIdAdapter : IExternalProviderAdapter
{
    private readonly ProviderAdapterCore core;
    public YandexIdAdapter(YandexIdOptions options, HttpClient? httpClient = null, TimeProvider? timeProvider = null)
        => core = new(false, options?.Metadata ?? throw new ExternalProviderException(ExternalProviderFailure.InvalidConfiguration), httpClient, timeProvider);
    public string Provider => core.Provider;
    public bool IsConfigured => core.IsConfigured;
    public Uri BuildAuthorizationUri(ProviderState state, CodeChallenge challenge) => core.BuildAuthorizationUri(state, challenge);
    public Task<VerifiedExternalIdentity> ExchangeIdentityAsync(string code, ProviderState state,
        CodeVerifier verifier, string? deviceId = null, CancellationToken ct = default)
        => core.ExchangeIdentityAsync(code, state, verifier, deviceId, ct);
    public void Dispose() => core.Dispose();
}
