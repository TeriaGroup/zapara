using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using System.Text.Json;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Zapara.Server.Accounts;

namespace Zapara.Server.Web;

public sealed partial class WebOAuth(AccountsDataSource source, AccountsConfiguration configuration,
    ExternalAuthService external, [FromKeyedServices(WebProtection.Key)] IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly string schema = configuration.QuotedSchema;
    private readonly IDataProtector protector = protection.CreateProtector("Zapara.Web.OAuth.v1");

    internal async Task<ExternalStartResponse> StartAsync(HttpContext context, string provider, WebExternalStartRequest request, string? access)
    {
        var browser = context.RequestServices.GetRequiredService<WebBrowserState>().Validate(context);
        var verifier = WebConfiguration.Random();
        var started = await external.StartBrowserAsync(provider, new(request.Purpose,
            WebEncoders.Base64UrlEncode(WebConfiguration.Hash(verifier)), "S256",
            new(Guid.NewGuid(), "Браузер Zapara", "web"), new("web"), request.ProofToken, request.ProofPurpose), access, context.RequestAborted);
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(context.RequestAborted);
        await using var command = new NpgsqlCommand($"INSERT INTO {schema}.web_oauth_flows(transaction_id,browser_hash,protected_verifier,expires_at) VALUES(@id,@browser,@verifier,@expires)", connection);
        command.Parameters.AddWithValue("id", started.TransactionId);
        command.Parameters.AddWithValue("browser", WebConfiguration.Hash(browser.Nonce));
        command.Parameters.AddWithValue("verifier", protector.Protect(verifier));
        command.Parameters.AddWithValue("expires", started.ExpiresAt);
        await command.ExecuteNonQueryAsync(context.RequestAborted);
        return started;
    }

    // Called from the already configured provider callback. A native transaction is left
    // entirely to its original handler; a browser transaction must match its initiating cookie.
    internal async Task<IResult?> TryCallbackAsync(HttpContext context, string provider)
    {
        var state = context.Request.Query["state"].ToString();
        if (!WebConfiguration.Token(state)) return null;
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(context.RequestAborted);
        Guid id;
        byte[] bound;
        string verifier;
        string purpose;
        await using (var command = new NpgsqlCommand($"""
            SELECT w.transaction_id,w.browser_hash,w.protected_verifier,t.purpose FROM {schema}.web_oauth_flows w
            JOIN {schema}.oauth_transactions t ON t.transaction_id=w.transaction_id
            WHERE t.state_hash=@state AND t.provider=@provider AND w.expires_at>@now
            """, connection))
        {
            command.Parameters.AddWithValue("state", WebConfiguration.Hash(state));
            command.Parameters.AddWithValue("provider", provider);
            command.Parameters.AddWithValue("now", clock.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted)) return null;
            id = reader.GetGuid(0);
            bound = reader.GetFieldValue<byte[]>(1);
            verifier = reader.GetString(2);
            purpose = reader.GetString(3);
        }
        var browser = context.RequestServices.GetRequiredService<WebBrowserState>().Read(context);
        if (browser is null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bound, WebConfiguration.Hash(browser.Nonce)))
            return Results.Text("Вход нужно завершить в том же браузере.", statusCode: 403);
        var query = context.Request.Query;
        var destination = await external.CallbackBrowserAsync(provider, new Uri(context.Request.GetEncodedUrl()), state,
            query["code"].FirstOrDefault(), bound, query["device_id"].FirstOrDefault(), query.ContainsKey("error"), context.RequestAborted);
        var handoff = QueryHelpers.ParseQuery(destination.Query)["handoffCode"].ToString();
        var exchange = new ExternalExchangeRequest(id, protector.Unprotect(verifier), handoff);
        ExternalExchangeResponse result;
        var store = context.RequestServices.GetRequiredService<WebSessionStore>();
        var hasCookie = context.Request.Cookies.ContainsKey(WebConfiguration.SessionCookie);
        // A fresh login must not leave the previous browser family alive. Link and reauth keep it.
        if (purpose == "login" && hasCookie)
        {
            try
            {
                await store.UseAsync(context, async token =>
                {
                    await context.RequestServices.GetRequiredService<AccountService>().LogoutAsync(token, context.RequestAborted);
                    return true;
                });
            }
            catch (WebRequestException e) when (e.Status == 401) { }
            catch (AccountServiceException e) when (e.Failure == AccountFailure.InvalidSession) { }
            await store.DeleteAsync(context);
            result = await external.ExchangeAsync(exchange, ct: context.RequestAborted);
        }
        else if (hasCookie)
            result = await store.UseAsync(context, token => external.ExchangeAsync(exchange, token, context.RequestAborted), bootstrap: true);
        else result = await external.ExchangeAsync(exchange, ct: context.RequestAborted);
        if (result.Session is { } session)
        {
            var next = context.RequestServices.GetRequiredService<WebBrowserState>().Create(context);
            bound = WebConfiguration.Hash(next.Nonce);
            var cookie = await context.RequestServices.GetRequiredService<WebSessionStore>().CreateAsync(session, next, context.RequestAborted);
            context.Response.Cookies.Append(WebConfiguration.SessionCookie, cookie, WebConfiguration.Cookie(session.RefreshExpiresAt));
        }
        await using (var save = new NpgsqlCommand($"UPDATE {schema}.web_oauth_flows SET protected_verifier='',protected_result=@result,browser_hash=@browser WHERE transaction_id=@id", connection))
        {
            save.Parameters.AddWithValue("id", id);
            save.Parameters.AddWithValue("browser", bound);
            save.Parameters.AddWithValue("result", protector.Protect(JsonSerializer.Serialize(new { result.Status, result.Proof }, AccountJson.CreateOptions())));
            await save.ExecuteNonQueryAsync(context.RequestAborted);
        }
        return Results.Redirect("/app/settings");
    }

    internal async Task<IResult> ResultAsync(HttpContext context, Guid id)
    {
        var browser = context.RequestServices.GetRequiredService<WebBrowserState>().Read(context) ?? throw new WebRequestException(401, "invalid_session");
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(context.RequestAborted);
        await using var command = new NpgsqlCommand($"""
            SELECT w.protected_result,t.status,w.expires_at FROM {schema}.web_oauth_flows w
            JOIN {schema}.oauth_transactions t ON t.transaction_id=w.transaction_id
            WHERE w.transaction_id=@id AND w.browser_hash=@browser
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("browser", WebConfiguration.Hash(browser.Nonce));
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted)) throw new WebRequestException(410, "external_attempt_expired");
        if (reader.GetFieldValue<DateTimeOffset>(2) <= clock.GetUtcNow()) return Results.Json(new { status = "expired" });
        if (!reader.IsDBNull(0)) return Results.Text(protector.Unprotect(reader.GetString(0)), "application/json");
        return Results.Json(new { status = reader.GetString(1) is "failed" or "expired" ? reader.GetString(1) : "pending" });
    }
}

public sealed record WebExternalStartRequest(string Purpose = "login", string? ProofToken = null, string? ProofPurpose = null)
{
    public override string ToString() => "WebExternalStartRequest { [REDACTED] }";
}
