using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Accounts;

public sealed class ExternalProviderRegistry : IDisposable
{
    private readonly object gate = new();
    private readonly Func<string>? stampOf;
    private readonly Func<(IReadOnlyList<IExternalProviderAdapter> Adapters, IReadOnlyDictionary<string, string> Callbacks)>? build;
    private List<IExternalProviderAdapter> adapters;
    private Dictionary<string, string> callbackUris;
    private string? stamp;
    private bool disposed;

    public ExternalProviderRegistry(IReadOnlyList<IExternalProviderAdapter> adapters, IReadOnlyDictionary<string, string> callbackUris,
        Func<string>? stampOf = null,
        Func<(IReadOnlyList<IExternalProviderAdapter> Adapters, IReadOnlyDictionary<string, string> Callbacks)>? build = null)
    {
        this.adapters = adapters.ToList();
        this.callbackUris = new Dictionary<string, string>(callbackUris, StringComparer.Ordinal);
        this.stampOf = stampOf;
        this.build = build;
    }

    public bool IsConfigured(string provider)
    {
        lock (gate)
        {
            Ensure();
            return Ready(provider);
        }
    }

    internal IExternalProviderAdapter Get(string provider)
    {
        lock (gate)
        {
            Ensure();
            if (provider is not ("vk" or "yandex") || !Ready(provider)) throw ExternalAuthException.Unavailable();
            var adapter = adapters.Single(item => item.Provider == provider);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(adapter.BuildAuthorizationUri(
                new(new string('a', 43)), new(new string('A', 43))).Query);
            if (query["redirect_uri"].ToString() != callbackUris[provider]) throw ExternalAuthException.Unavailable();
            return adapter;
        }
    }

    public string CallbackUri(string provider)
    {
        Get(provider);
        lock (gate) return callbackUris[provider];
    }

    public void VerifyUri(string provider, Uri incoming)
    {
        var expected = new Uri(CallbackUri(provider));
        if (incoming.Scheme != expected.Scheme || incoming.Authority != expected.Authority ||
            incoming.AbsolutePath != expected.AbsolutePath || incoming.UserInfo.Length != 0 || incoming.Fragment.Length != 0)
            throw ExternalAuthException.Invalid();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var adapter in adapters) adapter.Dispose();
            adapters = [];
            callbackUris = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private bool Ready(string provider) => !disposed
        && adapters.Any(item => item.Provider == provider && item.IsConfigured)
        && callbackUris.ContainsKey(provider);

    private void Ensure()
    {
        if (disposed || stampOf is null || build is null) return;
        string next;
        try { next = stampOf(); }
        catch (Exception) { return; }
        if (next == stamp) return;
        try
        {
            var built = build();
            foreach (var adapter in adapters) adapter.Dispose();
            adapters = built.Adapters.ToList();
            callbackUris = new Dictionary<string, string>(built.Callbacks, StringComparer.Ordinal);
            stamp = next;
        }
        catch (Exception)
        {
            // A bad saved row leaves the previous adapters in place.
        }
    }
}
