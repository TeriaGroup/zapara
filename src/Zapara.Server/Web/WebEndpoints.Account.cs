using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;

namespace Zapara.Server.Web;

internal static partial class WebEndpoints
{
    private static void MapAccount(RouteGroupBuilder group)
    {
        Route(group, "GET", "/account/me", async c => Json(await Service(c).GetMeAsync(Token(c), c.RequestAborted)));
        Route(group, "PATCH", "/account/me", async c => Json(await Service(c).UpdateProfileAsync(Token(c), await AccountBodyReader.Read<UpdateProfileRequest>(c), c.RequestAborted)));
        Route(group, "GET", "/account/devices", async c =>
        {
            var query = c.Request.Query;
            if (query.Any(p => p.Key is not ("limit" or "cursor") || p.Value.Count != 1)) throw new AccountBodyException();
            var limit = 20;
            if (query.ContainsKey("limit") && !int.TryParse(query["limit"], out limit)) throw new AccountBodyException();
            return Json(await Service(c).ListDevicesAsync(Token(c), limit, query["cursor"].FirstOrDefault(), c.RequestAborted, includeWeb: true));
        });
        Route(group, "DELETE", "/account/devices/{familyId}", async c =>
        {
            await AccountBodyReader.Empty(c);
            await Service(c).RevokeSessionAsync(Token(c), Id(c, "familyId"), c.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/account/sessions/revoke-all", async c =>
        {
            await AccountBodyReader.Empty(c);
            await Service(c).RevokeAllAsync(Token(c), c.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/account/password/change", async c =>
        {
            await Service(c).ChangePasswordAsync(Token(c), await AccountBodyReader.Read<ChangePasswordRequest>(c), c.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/account/reauthenticate", async c => Json(await External(c).PasswordProofAsync(Token(c), await AccountBodyReader.Read<PasswordProofRequest>(c), c.RequestAborted)), rate: "account-login");
        Route(group, "GET", "/account/identities", async c => Json(await External(c).IdentitiesAsync(Token(c), c.RequestAborted)));
        Route(group, "DELETE", "/account/identities/{provider}", async c =>
        {
            await External(c).UnlinkAsync(Token(c), (string)c.Request.RouteValues["provider"]!, (await AccountBodyReader.Read<ProofRequest>(c)).ProofToken, c.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/account/password/set", async c =>
        {
            await External(c).SetFirstPasswordAsync(Token(c), await AccountBodyReader.Read<FirstPasswordRequest>(c), c.RequestAborted);
            return Results.NoContent();
        });
        Route(group, "POST", "/account/recovery-email/start", async c =>
        {
            var body = await AccountBodyReader.Read<StartRecoveryEmailRequest>(c);
            var recovery = Recovery(c);
            if (!recovery.RecoveryEnabled) return AccountErrors.Problem(503, "recovery_unavailable");
            await recovery.StartEmailAsync(Token(c), body, c.RequestAborted);
            return Json(new { }, 202);
        });
        Route(group, "POST", "/account/recovery-email/confirm", async c =>
        {
            await Recovery(c).ConfirmEmailAsync(await AccountBodyReader.Read<ConfirmRecoveryEmailRequest>(c), c.RequestAborted);
            return Results.NoContent();
        }, authenticated: false);
        Route(group, "POST", "/auth/password-reset/request", async c =>
        {
            await Recovery(c).RequestResetAsync(await AccountBodyReader.Read<PasswordResetRequest>(c), c.RequestAborted);
            return Json(new { }, 202);
        }, authenticated: false, rate: "account-login");
        Route(group, "POST", "/auth/password-reset/confirm", async c =>
        {
            await Recovery(c).ConfirmResetAsync(await AccountBodyReader.Read<PasswordResetConfirmRequest>(c), c.RequestAborted);
            return Results.NoContent();
        }, authenticated: false, rate: "account-login");
        Route(group, "POST", "/account/exports", async c => Json(await Lifecycle(c).CreateExportAsync(Token(c), await AccountBodyReader.Read<ProofRequest>(c), c.RequestAborted), 202));
        Route(group, "GET", "/account/exports/{id}", async c => Json(await Lifecycle(c).GetExportAsync(Token(c), Id(c, "id"), c.RequestAborted)));
        Route(group, "GET", "/account/exports/{id}/download", async c =>
        {
            var (payload, name) = await Lifecycle(c).DownloadAsync(Token(c), Id(c, "id"), c.RequestAborted);
            return Results.File(payload, "application/json", name);
        });
        Route(group, "DELETE", "/account", async c => Json(await Lifecycle(c).DeleteAccountAsync(Token(c), await AccountBodyReader.Read<ProofRequest>(c), c.RequestAborted), 202));
    }
    private static ExternalAuthService External(HttpContext c) => c.RequestServices.GetRequiredService<ExternalAuthService>();
    private static RecoveryService Recovery(HttpContext c) => c.RequestServices.GetRequiredService<RecoveryService>();
    private static AccountLifecycleService Lifecycle(HttpContext c) => c.RequestServices.GetRequiredService<AccountLifecycleService>();
}
