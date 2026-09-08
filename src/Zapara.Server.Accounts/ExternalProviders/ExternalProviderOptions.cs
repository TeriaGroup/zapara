namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class VkIdOptions
{
    public VkIdOptions(string? clientId = null, string? callbackUri = null, string? vkServiceToken = null)
        => Metadata = new(clientId, callbackUri, vkServiceToken);
    internal ProviderMetadata Metadata { get; }
    public string? ClientId => Metadata.ClientId;
    public string? CallbackUri => Metadata.CallbackUri;
    public override string ToString() => "VkIdOptions [redacted]";
}

public sealed class YandexIdOptions
{
    public YandexIdOptions(string? clientId = null, string? callbackUri = null, string? yandexClientSecret = null)
        => Metadata = new(clientId, callbackUri, yandexClientSecret);
    internal ProviderMetadata Metadata { get; }
    public string? ClientId => Metadata.ClientId;
    public string? CallbackUri => Metadata.CallbackUri;
    public override string ToString() => "YandexIdOptions [redacted]";
}

internal sealed class ProviderMetadata
{
    internal ProviderMetadata(string? clientId, string? callbackUri, string? secret)
    {
        if (clientId is null && callbackUri is null && secret is null) return;
        const ExternalProviderFailure failure = ExternalProviderFailure.InvalidConfiguration;
        ClientId = ProviderValidation.Printable(clientId, 256, failure);
        if (callbackUri is null || callbackUri.Length > 2048 ||
            !Uri.TryCreate(callbackUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            callbackUri.Any(c => c <= ' ' || c >= 127 || c == '\\'))
            throw new ExternalProviderException(failure);
        // Preserve exact operator registration, including percent-encoding, in both requests.
        CallbackUri = callbackUri;
        Secret = secret is null ? null : ProviderValidation.Printable(secret, 8192, failure);
    }
    internal string? ClientId { get; }
    internal string? CallbackUri { get; }
    internal string? Secret { get; }
    internal bool IsConfigured => ClientId is not null;
    public override string ToString() => "ProviderMetadata [redacted]";
}

internal static class ProviderValidation
{
    internal static string UrlSafe(string? value, int min, int max)
    {
        if (value is null || value.Length < min || value.Length > max ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ExternalProviderException(ExternalProviderFailure.InvalidRequest);
        return value;
    }

    internal static string Printable(string? value, int max, ExternalProviderFailure failure)
    {
        if (string.IsNullOrEmpty(value) || value.Length > max || value.Any(c => c < '!' || c > '~'))
            throw new ExternalProviderException(failure);
        return value;
    }
}
