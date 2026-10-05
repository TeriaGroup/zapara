using Microsoft.AspNetCore.Http.Extensions;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Server.Accounts;

internal sealed record ExternalEndpoint;

internal static class ExternalAuthEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1").WithMetadata(new AccountEndpoint(), new ExternalEndpoint());
        Route(group, "GET", "/auth/capabilities", c =>
        {
            return Task.FromResult(Json(AccountCapabilities.Read(c.RequestServices)));
        });
        Route(group, "POST", "/auth/external/{provider}/start", async c =>
        {
            var provider = Provider(c);
            if (!Registry(c).IsConfigured(provider)) return Error(503, "provider_unavailable");
            return Json(await Service(c).StartAsync(provider, await AccountBodyReader.Read<ExternalStartRequest>(c), OptionalBearer(c), c.RequestAborted));
        }, rate: "account-login");
        Route(group, "POST", "/auth/external/exchange", async c => Json(await Service(c).ExchangeAsync(
            await AccountBodyReader.Read<ExternalExchangeRequest>(c), OptionalBearer(c), c.RequestAborted)), rate: "account-refresh");
        Route(group, "GET", "/auth/external/{transactionId:guid}/status", async c => Json(await Service(c).StatusAsync(
            Guid.Parse((string)c.Request.RouteValues["transactionId"]!), c.RequestAborted)));
        Route(group, "POST", "/account/reauthenticate", async c => Json(await Service(c).PasswordProofAsync(
            OpaqueAccountHandler.Bearer(c.Request), await AccountBodyReader.Read<PasswordProofRequest>(c), c.RequestAborted)), true, "account-login");
        Route(group, "GET", "/account/identities", async c => Json(await Service(c).IdentitiesAsync(OpaqueAccountHandler.Bearer(c.Request), c.RequestAborted)), true);
        Route(group, "DELETE", "/account/identities/{provider}", async c =>
        {
            await Service(c).UnlinkAsync(OpaqueAccountHandler.Bearer(c.Request), Provider(c),
                (await AccountBodyReader.Read<ProofRequest>(c)).ProofToken, c.RequestAborted);
            return Results.NoContent();
        }, true);
        Route(group, "POST", "/account/password/set", async c =>
        {
            await Service(c).SetFirstPasswordAsync(OpaqueAccountHandler.Bearer(c.Request), await AccountBodyReader.Read<FirstPasswordRequest>(c), c.RequestAborted);
            return Results.NoContent();
        }, true, "account-login");
        var registry = app.Services.GetRequiredService<ExternalProviderRegistry>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in new[] { "vk", "yandex" })
        {
            // The canonical path stays registered after a settings change. A callback that
            // existed only while the provider was configured at startup came back as an empty 404.
            var paths = new List<string> { "/auth/" + provider + "/callback" };
            if (registry.IsConfigured(provider)) paths.Add(new Uri(registry.CallbackUri(provider)).AbsolutePath);
            foreach (var path in paths)
            {
                if (!mapped.Add(path)) continue;
                var name = provider;
                var callbackGroup = app.MapGroup("").WithMetadata(new AccountEndpoint(), new ExternalEndpoint());
                Route(callbackGroup, "GET", path, c =>
                    Registry(c).IsConfigured(name) ? Callback(c, name) : Task.FromResult(Page(503)), rate: "account-refresh", callback: true);
            }
        }
    }

    private static async Task<IResult> Callback(HttpContext c, string provider)
    {
        if (c.Request.QueryString.Value?.Length > 16384) throw new AccountBodyException();
        var query = c.Request.Query;
        if (query.Any(p => p.Value.Count != 1) ||
            query.Keys.Any(k => k is not ("state" or "code" or "device_id" or "error" or "error_description" or "cid"))) throw new AccountBodyException();
        string? Value(string key) => query.TryGetValue(key, out var value) ? value.ToString() : null;
        if (c.RequestServices.GetService<Zapara.Server.Web.WebOAuth>() is { } web)
        {
            var browserResult = await web.TryCallbackAsync(c, provider);
            if (browserResult is not null) return browserResult;
        }
        var destination = await Service(c).CallbackAsync(provider, new Uri(c.Request.GetEncodedUrl()), Value("state")!,
            Value("code"), Value("device_id"), query.ContainsKey("error"), c.RequestAborted);
        if (destination.Scheme == "zapara")
        {
            var link = System.Net.WebUtility.HtmlEncode(destination.AbsoluteUri);
            var html = $$"""
                <!DOCTYPE html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Вход</title></head>
                <body style="margin:0;background:#0d0d0d;color:#f2f2f2;font-family:sans-serif"><main style="max-width:28rem;margin:0 auto;padding:32px 20px">
                <h1 style="font-size:22px;font-weight:500">Подтверждение получено</h1>
                <p style="font-size:15px;line-height:1.4">Вернитесь в приложение для завершения входа. Если оно не открылось, нажмите кнопку. Дождитесь подтверждения входа в настройках.</p>
                <p><a href="{{link}}" style="display:inline-flex;align-items:center;min-height:48px;padding:0 16px;border-radius:8px;background:#f2f2f2;color:#0d0d0d;text-decoration:none;font-weight:500">Открыть приложение</a></p>
                </main></body></html>
                """;
            return Results.Content(html, "text/html; charset=utf-8");
        }
        c.Response.Headers.Location = destination.AbsoluteUri;
        return Results.Text("Вход подтверждён. Вернитесь в приложение.", "text/plain; charset=utf-8", statusCode: 302);
    }

    private static void Route(RouteGroupBuilder group, string method, string path, Func<HttpContext, Task<IResult>> handler,
        bool authenticated = false, string rate = "account-other", bool callback = false)
    {
        var endpoint = group.MapMethods(path, [method], (Delegate)(Func<HttpContext, Task<IResult>>)(async c =>
        {
            try
            {
                if (c.Request.ContentLength > AccountBodyReader.MaximumBytes) throw new AccountBodyException(413);
                return await handler(c);
            }
            catch (ExternalAuthException e) { return callback ? Page(e.Status, e.Code) : Error(e.Status, e.Code); }
            catch (AccountBodyException e) { return callback ? Page(e.Status) : Error(e.Status, "invalid_request"); }
            catch (AccountServiceException e) { return callback ? Page(503) : AccountErrors.From(e); }
            catch (ArgumentException) { return callback ? Page(403) : Error(403, "invalid_external_proof"); }
            catch (Zapara.Server.Web.WebRequestException e) { return callback ? Page(e.Status) : Error(e.Status, e.Code); }
        })).RequireRateLimiting(rate);
        if (authenticated) endpoint.RequireAuthorization("AccountUser"); else endpoint.AllowAnonymous();
    }
    private static IResult Page(int status, string? code = null) => Results.Text(
        code == "registration_unavailable"
            ? "Создание нового аккаунта закрыто. Войдите в ранее привязанный аккаунт или обратитесь к администрации приложения."
            : "Вход не завершён. Начните новую попытку в приложении.",
        "text/plain; charset=utf-8", statusCode: status);
    private static IResult Error(int status, string code) => Results.Json(new AccountError("Не удалось выполнить вход.", status, code), AccountJson.CreateOptions(), "application/problem+json", status);
    private static IResult Json<T>(T value) => Results.Json(value, AccountJson.CreateOptions());
    private static string Provider(HttpContext c) => (string)c.Request.RouteValues["provider"]!;
    private static string? OptionalBearer(HttpContext c) => c.Request.Headers.ContainsKey("Authorization") ? OpaqueAccountHandler.Bearer(c.Request) : null;
    private static ExternalAuthService Service(HttpContext c) => c.RequestServices.GetRequiredService<ExternalAuthService>();
    private static ExternalProviderRegistry Registry(HttpContext c) => c.RequestServices.GetRequiredService<ExternalProviderRegistry>();
}
