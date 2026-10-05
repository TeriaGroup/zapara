using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Zapara.Server.Accounts;

namespace Zapara.Server.Web;

internal sealed record BrowserState(string Nonce, string CsrfToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "BrowserState { [REDACTED] }";
}

public sealed class WebBrowserState([FromKeyedServices(WebProtection.Key)] IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Zapara.Web.Browser.v1");
    internal BrowserState? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(WebConfiguration.BrowserCookie, out var cookie) || cookie.Length > 2048) return null;
        try
        {
            var state = JsonSerializer.Deserialize<BrowserState>(protector.Unprotect(cookie));
            return state is not null && state.ExpiresAt > clock.GetUtcNow() && WebConfiguration.Token(state.Nonce)
                && WebConfiguration.Token(state.CsrfToken) ? state : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException) { return null; }
    }
    internal BrowserState Prepare()
        => new(WebConfiguration.Random(), WebConfiguration.Random(), clock.GetUtcNow().AddDays(30));

    internal void Write(HttpContext context, BrowserState state)
        => context.Response.Cookies.Append(WebConfiguration.BrowserCookie, protector.Protect(JsonSerializer.Serialize(state)), WebConfiguration.Cookie(state.ExpiresAt));

    internal BrowserState Create(HttpContext context)
    {
        var state = Prepare();
        Write(context, state);
        return state;
    }
    internal BrowserState Validate(HttpContext context)
    {
        var state = Read(context);
        var supplied = context.Request.Headers[WebConfiguration.CsrfHeader];
        if (!WebConfiguration.SameOrigin(context.Request) || state is null || supplied.Count != 1 || !WebConfiguration.Token(supplied[0])
            || !CryptographicOperations.FixedTimeEquals(WebConfiguration.Hash(supplied[0]!), WebConfiguration.Hash(state.CsrfToken)))
            throw new WebRequestException(403, "csrf_invalid");
        return state;
    }
}

internal sealed class WebRequestException(int status, string code) : Exception("Браузерный запрос отклонён.")
{
    internal int Status { get; } = status;
    internal string Code { get; } = code;
}
