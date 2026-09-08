using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Accounts;

public sealed class ExternalProviderRegistry(IReadOnlyList<IExternalProviderAdapter> adapters,
    IReadOnlyDictionary<string, string> callbackUris) : IDisposable
{
    public bool IsConfigured(string provider) => adapters.Any(a => a.Provider == provider && a.IsConfigured)
        && callbackUris.ContainsKey(provider);
    internal IExternalProviderAdapter Get(string provider)
    {
        if (provider is not ("vk" or "yandex") || !IsConfigured(provider)) throw ExternalAuthException.Unavailable();
        var adapter = adapters.Single(a => a.Provider == provider);
        // The authorization redirect and HTTP route must use the same registered descriptor.
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(adapter.BuildAuthorizationUri(
            new(new string('a', 43)), new(new string('A', 43))).Query);
        if (query["redirect_uri"].ToString() != callbackUris[provider]) throw ExternalAuthException.Unavailable();
        return adapter;
    }
    public string CallbackUri(string provider) { Get(provider); return callbackUris[provider]; }
    public void VerifyUri(string provider, Uri incoming)
    {
        var expected = new Uri(CallbackUri(provider));
        if (incoming.Scheme != expected.Scheme || incoming.Authority != expected.Authority ||
            incoming.AbsolutePath != expected.AbsolutePath || incoming.UserInfo.Length != 0 || incoming.Fragment.Length != 0)
            throw ExternalAuthException.Invalid();
    }
    public void Dispose() { foreach (var adapter in adapters) adapter.Dispose(); }
}
