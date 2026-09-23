using Npgsql;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;

namespace Zapara.Server.Web;

internal static partial class WebEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/web-api");
        Route(group, "GET", "/session", async c =>
        {
            var browser = c.RequestServices.GetRequiredService<WebBrowserState>();
            var state = browser.Read(c) ?? browser.Create(c);
            MeResponse? me = null;
            if (c.Request.Cookies.ContainsKey(WebConfiguration.SessionCookie) && c.RequestServices.GetService<WebSessionStore>() is { } store)
            {
                try { me = await store.UseAsync(c, token => Service(c).GetMeAsync(token, c.RequestAborted), bootstrap: true); }
                catch (WebRequestException e) when (e.Status == 401) { await store.DeleteAsync(c); }
                catch (AccountServiceException e) when (e.Failure == AccountFailure.InvalidSession) { await store.DeleteAsync(c); }
            }
            return Json(new { authenticated = me is not null, user = me?.User, familyId = me?.FamilyId,
                csrfToken = state.CsrfToken, capabilities = AccountCapabilities.Read(c.RequestServices) });
        }, authenticated: false);
        MapNotifications(group);
        if (!AccountsConfiguration.IsEnabled(app.Configuration)) return;
        Route(group, "GET", "/auth/capabilities", c => Task.FromResult(Json(AccountCapabilities.Read(c.RequestServices))), authenticated: false);
        Route(group, "POST", "/auth/register", async c =>
        {
            if (!AccountCapabilities.Read(c.RequestServices).Registration) return AccountErrors.Problem(503, "registration_unavailable");
            return Json(await Service(c).RegisterAsync(await AccountBodyReader.Read<RegisterRequest>(c), c.RequestAborted), 201);
        }, authenticated: false, rate: "account-register");
        Route(group, "POST", "/auth/login", async c =>
        {
            var request = await AccountBodyReader.Read<WebLoginRequest>(c);
            // Explicit login is also an account switch: revoke the previous browser family.
            if (c.Request.Cookies.ContainsKey(WebConfiguration.SessionCookie))
            {
                try { await Store(c).UseAsync(c, async token => { await Service(c).LogoutAsync(token, c.RequestAborted); return true; }, bootstrap: true); }
                catch (WebRequestException e) when (e.Status == 401) { }
                catch (AccountServiceException e) when (e.Failure == AccountFailure.InvalidSession) { }
                await Store(c).DeleteAsync(c);
            }
            var session = await Service(c).LoginAsync(new(request.Username, request.Password,
                new DeviceInput(Guid.NewGuid(), request.DeviceName ?? "Браузер «Расписание военмех»", "web")), c.RequestAborted);
            return await CompleteLogin(c, session);
        }, authenticated: false, rate: "account-login");
        Route(group, "POST", "/auth/logout", async c =>
        {
            await AccountBodyReader.Empty(c);
            await Service(c).LogoutAsync(Token(c), c.RequestAborted);
            await Store(c).DeleteAsync(c);
            c.RequestServices.GetRequiredService<WebBrowserState>().Create(c);
            return Results.NoContent();
        });
        Route(group, "POST", "/auth/refresh", async c =>
        {
            var me = await Service(c).GetMeAsync(Token(c), c.RequestAborted);
            var browser = c.RequestServices.GetRequiredService<WebBrowserState>().Read(c)!;
            return Json(new { authenticated = true, me.User, me.FamilyId, browser.CsrfToken,
                capabilities = AccountCapabilities.Read(c.RequestServices) });
        }, rate: "account-refresh", rotate: true);
        MapAccount(group);
        Route(group, "POST", "/auth/external/{provider}/start", async c =>
        {
            var body = await AccountBodyReader.Read<WebExternalStartRequest>(c);
            var oauth = c.RequestServices.GetRequiredService<WebOAuth>();
            var provider = (string)c.Request.RouteValues["provider"]!;
            if (body.Purpose != "login") return await Store(c).UseAsync(c, async token => Json(await oauth.StartAsync(c, provider, body, token)));
            if (c.Request.Cookies.ContainsKey(WebConfiguration.SessionCookie))
            {
                try { await Store(c).UseAsync(c, async token => { await Service(c).LogoutAsync(token, c.RequestAborted); return true; }); }
                catch (WebRequestException e) when (e.Status == 401) { }
                catch (AccountServiceException e) when (e.Failure == AccountFailure.InvalidSession) { }
                await Store(c).DeleteAsync(c);
            }
            return Json(await oauth.StartAsync(c, provider, body, null));
        }, authenticated: false, rate: "account-login");
        Route(group, "GET", "/auth/external/{transactionId}/result", c => c.RequestServices.GetRequiredService<WebOAuth>().ResultAsync(c, Id(c, "transactionId")), authenticated: false);
        Route(group, "POST", "/auth/external/{transactionId}/cancel", async c =>
        {
            await AccountBodyReader.Empty(c);
            await c.RequestServices.GetRequiredService<WebOAuth>().CancelAsync(c, Id(c, "transactionId"));
            return Results.NoContent();
        }, authenticated: false);
        MapSync(group, app.Configuration);
        MapCommunities(group, app.Configuration);
        MapSocial(group);
    }

    private static async Task<IResult> CompleteLogin(HttpContext c, SessionResponse session)
    {
        var state = c.RequestServices.GetRequiredService<WebBrowserState>().Create(c);
        var id = await Store(c).CreateAsync(session, state, c.RequestAborted);
        c.Response.Cookies.Append(WebConfiguration.SessionCookie, id, WebConfiguration.Cookie(session.RefreshExpiresAt));
        return Json(new { authenticated = true, session.User, session.FamilyId, state.CsrfToken,
            capabilities = AccountCapabilities.Read(c.RequestServices) });
    }

    private static void Route(RouteGroupBuilder group, string method, string path, Func<HttpContext, Task<IResult>> handler,
        bool authenticated = true, string rate = "account-other", bool rotate = false, bool bootstrap = false)
    {
        var endpoint = group.MapMethods(path, [method], (Delegate)(Func<HttpContext, Task<IResult>>)(async c =>
        {
            c.Response.Headers.CacheControl = "no-store";
            c.Response.Headers["Referrer-Policy"] = "no-referrer";
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try
            {
                if (!HttpMethods.IsGet(method)) c.RequestServices.GetRequiredService<WebBrowserState>().Validate(c);
                else if (c.Request.Headers["Sec-Fetch-Site"].ToString() is "cross-site" or "same-site"
                    || (c.Request.Headers.ContainsKey("Origin") && !WebConfiguration.SameOrigin(c.Request))) throw new WebRequestException(403, "csrf_invalid");
                if (authenticated)
                    return await Store(c).UseAsync(c, async token => { c.Items[typeof(WebSessionStore)] = token; return await handler(c); }, bootstrap: bootstrap, rotate: rotate);
                return await handler(c);
            }
            catch (WebRequestException e) { return AccountErrors.Problem(e.Status, e.Code); }
            catch (AccountBodyException e) { return AccountErrors.Problem(e.Status, "invalid_request"); }
            catch (AccountServiceException e) { return AccountErrors.From(e); }
            catch (ExternalAuthException e) { return AccountErrors.Problem(e.Status, e.Code); }
            catch (Zapara.Server.Notifications.PushOperationException e) { return AccountErrors.Problem(e.Status, e.Code); }
            catch (Zapara.Server.Sync.SyncInputException e) { return Zapara.Server.Sync.SyncHttpResult.Error(new(e.Status, e.Status == 413 ? "payload_too_large" : "invalid_request")); }
            catch (Zapara.Server.Communities.CommunityInputException e) { return Zapara.Server.Communities.CommunityHttpResult.Problem(e.Status, e.Status == 413 ? "payload_too_large" : "invalid_request"); }
            catch (Zapara.Server.Communities.CommunityServiceException e) { return Zapara.Server.Communities.CommunityHttpResult.From(e); }
            catch (Zapara.Server.Social.SocialException e) { return SocialHttp.Problem(e); }
            catch (NpgsqlException) { return AccountErrors.Problem(503, "db_unavailable"); }
            finally { c.Items.Remove(typeof(WebSessionStore)); }
        })).AllowAnonymous(); // Browser routes are authorized above, never by the native bearer handler.
        if (AccountsConfiguration.IsEnabled(((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<IConfiguration>())) endpoint.RequireRateLimiting(rate);
    }
    private static WebSessionStore Store(HttpContext c) => c.RequestServices.GetService<WebSessionStore>() ?? throw new WebRequestException(401, "invalid_session");
    private static AccountService Service(HttpContext c) => c.RequestServices.GetRequiredService<AccountService>();
    private static string Token(HttpContext c) => (string)c.Items[typeof(WebSessionStore)]!;
    private static IResult Json<T>(T value, int status = 200) => Results.Json(value, AccountJson.CreateOptions(), statusCode: status);
    private static Guid Id(HttpContext c, string name)
    {
        var raw = c.Request.RouteValues[name] as string;
        if (!Guid.TryParseExact(raw, "D", out var id) || id == Guid.Empty || raw != id.ToString("D")) throw new AccountBodyException();
        return id;
    }
}

public sealed record WebLoginRequest
{
    [System.Text.Json.Serialization.JsonConstructor]
    public WebLoginRequest(string username, string password, string? deviceName = null)
        => (Username, Password, DeviceName) = (AccountValidation.Username(username), AccountValidation.Password(password),
            deviceName is null ? null : AccountValidation.DeviceName(deviceName));
    public string Username { get; }
    public string Password { get; }
    public string? DeviceName { get; }
    public override string ToString() => "WebLoginRequest { [REDACTED] }";
}
