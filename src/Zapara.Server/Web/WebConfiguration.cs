using System.Security.Cryptography;
using System.Text;

namespace Zapara.Server.Web;

public static class WebConfiguration
{
    public const string SessionCookie = "__Host-ZaparaWeb";
    public const string BrowserCookie = "__Host-ZaparaBrowser";
    public const string CsrfHeader = "X-Zapara-CSRF";
    public const string FamilyHeader = "X-Zapara-Family";
    public static bool Enabled(IConfiguration configuration) => bool.TryParse(configuration["Web:Enabled"], out var enabled) && enabled;
    internal static string Random() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    internal static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    internal static bool Token(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    internal static CookieOptions Cookie(DateTimeOffset expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true, Expires = expires
    };
    internal static bool SameOrigin(HttpRequest request)
    {
        if (request.Headers["Sec-Fetch-Site"].ToString() is "cross-site" or "same-site") return false;
        var origin = request.Headers.Origin;
        return origin.Count == 1 && Uri.TryCreate(origin[0], UriKind.Absolute, out var parsed)
            && parsed.GetLeftPart(UriPartial.Authority) == request.Scheme + "://" + request.Host
            && parsed.AbsolutePath == "/" && parsed.Query.Length == 0 && parsed.Fragment.Length == 0;
    }
}
