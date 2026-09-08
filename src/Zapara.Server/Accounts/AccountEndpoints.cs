using System.Globalization;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;

namespace Zapara.Server.Accounts;

internal static class AccountEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1").WithMetadata(new AccountEndpoint());
        Route(group, "POST", "/auth/register", Register, "account-register", false);
        Route(group, "POST", "/auth/login", async context =>
        {
            var body = await AccountBodyReader.Read<LoginRequest>(context);
            return Json(await Service(context).LoginAsync(body, context.RequestAborted));
        }, "account-login", false);
        Route(group, "POST", "/auth/refresh", async context =>
        {
            var token = await AccountBodyReader.Refresh(context);
            return Json(await Service(context).RefreshAsync(token, context.RequestAborted));
        }, "account-refresh", false);
        Route(group, "GET", "/account/me", async context =>
            Json(await Service(context).GetMeAsync(Bearer(context), context.RequestAborted)));
        Route(group, "PATCH", "/account/me", async context =>
        {
            var body = await AccountBodyReader.Read<UpdateProfileRequest>(context);
            return Json(await Service(context).UpdateProfileAsync(Bearer(context), body, context.RequestAborted));
        });
        Route(group, "GET", "/account/devices", Devices);
        Route(group, "DELETE", "/account/devices/{familyId}", async context =>
        {
            var raw = context.Request.RouteValues["familyId"] as string;
            if (!Guid.TryParseExact(raw, "D", out var id) || id == Guid.Empty || raw != id.ToString("D"))
                throw new AccountBodyException();
            await AccountBodyReader.Empty(context);
            await Service(context).RevokeSessionAsync(Bearer(context), id, context.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/auth/logout", context => Empty(context,
            service => service.LogoutAsync(Bearer(context), context.RequestAborted)));
        Route(group, "POST", "/account/sessions/revoke-all", context => Empty(context,
            service => service.RevokeAllAsync(Bearer(context), context.RequestAborted)));
        Route(group, "POST", "/account/password/change", async context =>
        {
            var body = await AccountBodyReader.Read<ChangePasswordRequest>(context);
            await Service(context).ChangePasswordAsync(Bearer(context), body, context.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/account/recovery-email/start", async context =>
        {
            var body = await AccountBodyReader.Read<StartRecoveryEmailRequest>(context);
            var recovery = Recovery(context);
            if (!recovery.RecoveryEnabled) return AccountErrors.Problem(503, "recovery_unavailable");
            await recovery.StartEmailAsync(Bearer(context), body, context.RequestAborted);
            return Json(new Dictionary<string, string?>(), 202);
        });
        Route(group, "POST", "/account/recovery-email/confirm", async context =>
        {
            await Recovery(context).ConfirmEmailAsync(await AccountBodyReader.Read<ConfirmRecoveryEmailRequest>(context), context.RequestAborted);
            return Results.NoContent();
        }, "account-login", false);
        Route(group, "POST", "/auth/password-reset/request", async context =>
        {
            await Recovery(context).RequestResetAsync(await AccountBodyReader.Read<PasswordResetRequest>(context), context.RequestAborted);
            return Json(new Dictionary<string, string?>(), 202);
        }, "account-login", false);
        Route(group, "POST", "/auth/password-reset/confirm", async context =>
        {
            await Recovery(context).ConfirmResetAsync(await AccountBodyReader.Read<PasswordResetConfirmRequest>(context), context.RequestAborted);
            return Results.NoContent();
        }, "account-login", false);
        Route(group, "POST", "/account/exports", async context =>
        {
            var body = await AccountBodyReader.Read<ProofRequest>(context);
            return Json(await Lifecycle(context).CreateExportAsync(Bearer(context), body, context.RequestAborted), 202);
        });
        Route(group, "GET", "/account/exports/{id}", async context =>
            Json(await Lifecycle(context).GetExportAsync(Bearer(context), ParseId(context), context.RequestAborted)));
        Route(group, "GET", "/account/exports/{id}/download", async context =>
        {
            var (payload, name) = await Lifecycle(context).DownloadAsync(Bearer(context), ParseId(context), context.RequestAborted);
            return Results.File(payload, "application/json", name);
        });
        Route(group, "DELETE", "/account", async context =>
        {
            var body = await AccountBodyReader.Read<ProofRequest>(context);
            return Json(await Lifecycle(context).DeleteAccountAsync(Bearer(context), body, context.RequestAborted), 202);
        });
    }

    private static async Task<IResult> Register(HttpContext context)
    {
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            return AccountErrors.Problem(503, "registration_unavailable");
        var body = await AccountBodyReader.Read<RegisterRequest>(context);
        return Json(await Service(context).RegisterAsync(body, context.RequestAborted), 201);
    }

    private static async Task<IResult> Devices(HttpContext context)
    {
        var query = context.Request.Query;
        if (query.Keys.Any(key => key is not ("limit" or "cursor"))) throw new AccountBodyException();
        var limit = 20;
        if (query.TryGetValue("limit", out var raw) && (raw.Count != 1 ||
            !int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
            throw new AccountBodyException();
        var cursor = query["cursor"];
        if (cursor.Count > 1 || (cursor.Count == 1 && cursor[0]?.Length != 55)) throw new AccountBodyException();
        return Json(await Service(context).ListDevicesAsync(Bearer(context), limit, cursor.Count == 0 ? null : cursor[0], context.RequestAborted));
    }

    private static async Task<IResult> Empty(HttpContext context, Func<AccountService, Task> operation)
    {
        await AccountBodyReader.Empty(context);
        await operation(Service(context));
        return Results.NoContent();
    }

    private static void Route(RouteGroupBuilder group, string method, string path,
        Func<HttpContext, Task<IResult>> handler, string rate = "account-other", bool authenticated = true)
    {
        // Explicit Delegate overload: RequestDelegate would discard Task<IResult>'s response.
        var endpoint = group.MapMethods(path, new[] { method }, (Delegate)(Func<HttpContext, Task<IResult>>)(async context =>
        {
            try { return await handler(context); }
            catch (AccountBodyException exception) { return AccountErrors.Problem(exception.Status, "invalid_request"); }
            catch (ExternalAuthException exception) { return AccountErrors.Problem(exception.Status, exception.Code); }
            catch (AccountServiceException exception)
            {
                if (exception.Failure == AccountFailure.RateLimited) context.Response.Headers.RetryAfter = "60";
                return AccountErrors.From(exception);
            }
        })).RequireRateLimiting(rate);
        if (authenticated) endpoint.RequireAuthorization("AccountUser");
        else endpoint.AllowAnonymous();
    }

    private static AccountService Service(HttpContext context) => context.RequestServices.GetRequiredService<AccountService>();
    private static RecoveryService Recovery(HttpContext context) => context.RequestServices.GetRequiredService<RecoveryService>();
    private static AccountLifecycleService Lifecycle(HttpContext context) => context.RequestServices.GetRequiredService<AccountLifecycleService>();
    private static string Bearer(HttpContext context) => OpaqueAccountHandler.Bearer(context.Request);
    private static Guid ParseId(HttpContext context)
    {
        var raw = context.Request.RouteValues["id"] as string;
        if (!Guid.TryParseExact(raw, "D", out var id) || id == Guid.Empty || raw != id.ToString("D"))
            throw new AccountBodyException();
        return id;
    }
    private static IResult Json<T>(T value, int status = 200) => Results.Json(value, AccountJson.CreateOptions(), statusCode: status);
}
