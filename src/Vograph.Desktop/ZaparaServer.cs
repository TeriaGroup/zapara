namespace Vograph.Desktop;

/// <summary>The public Zapara host. An explicit empty environment variable keeps the guest offline.</summary>
public static class ZaparaServer
{
    public const string DefaultBaseUrl = "https://voen.teriahost.ru";

    public static string? AccountBaseUrl()
    {
        var account = Environment.GetEnvironmentVariable("VOGRAPH_ACCOUNT_BASE_URL");
        if (account is not null) return Blank(account);
        var api = Environment.GetEnvironmentVariable("VOGRAPH_API_BASE_URL");
        if (api is not null) return Blank(api);
        return DefaultBaseUrl;
    }

    public static string? TimetableBaseUrl()
    {
        var api = Environment.GetEnvironmentVariable("VOGRAPH_API_BASE_URL");
        return api is null ? DefaultBaseUrl : Blank(api);
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
