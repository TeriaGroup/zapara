using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;

namespace Zapara.Server.Web;

// Only a random opaque identifier leaves the server. Database values are protected by a
// dedicated purpose; the native family remains authoritative for every domain operation.
public sealed class WebSessionStore(AccountsDataSource source, AccountsConfiguration configuration,
    AccountService accounts, [FromKeyedServices(WebProtection.Key)] IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Zapara.Web.Tokens.v1");
    private readonly string schema = configuration.QuotedSchema;

    internal async Task<string> CreateAsync(SessionResponse session, BrowserState browser, CancellationToken ct)
    {
        var id = WebConfiguration.Random();
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {schema}.web_sessions(session_hash,family_id,protected_tokens,csrf_hash,created_at,expires_at)
            VALUES(@id,@family,@tokens,@csrf,@now,@expires)
            """, connection);
        command.Parameters.AddWithValue("id", WebConfiguration.Hash(id));
        command.Parameters.AddWithValue("family", session.FamilyId);
        command.Parameters.AddWithValue("tokens", protector.Protect(JsonSerializer.Serialize(session, AccountJson.CreateOptions())));
        command.Parameters.AddWithValue("csrf", WebConfiguration.Hash(browser.CsrfToken));
        command.Parameters.AddWithValue("now", clock.GetUtcNow());
        command.Parameters.AddWithValue("expires", session.RefreshExpiresAt);
        await command.ExecuteNonQueryAsync(ct);
        return id;
    }

    internal async Task<T> UseAsync<T>(HttpContext context, Func<string, Task<T>> operation, bool bootstrap = false, bool rotate = false)
    {
        var ct = context.RequestAborted;
        var id = context.Request.Cookies[WebConfiguration.SessionCookie];
        if (!WebConfiguration.Token(id)) throw new WebRequestException(401, "invalid_session");
        var hash = WebConfiguration.Hash(id!);
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(ct);
        // Transaction advisory lock serializes all tabs and all server instances, including
        // token rotation, logout and switching. No process-local semaphore can provide this.
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, tx))
        {
            gate.Parameters.AddWithValue("key", BinaryPrimitives.ReadInt64BigEndian(hash));
            await gate.ExecuteNonQueryAsync(ct);
        }
        SessionResponse session;
        byte[] csrf;
        await using (var command = new NpgsqlCommand($"SELECT protected_tokens,csrf_hash FROM {schema}.web_sessions WHERE session_hash=@id AND expires_at>@now", connection, tx))
        {
            command.Parameters.AddWithValue("id", hash);
            command.Parameters.AddWithValue("now", clock.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new WebRequestException(401, "invalid_session");
            try { session = JsonSerializer.Deserialize<SessionResponse>(protector.Unprotect(reader.GetString(0)), AccountJson.CreateOptions())!; }
            catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException)
            { throw new WebRequestException(401, "invalid_session"); }
            csrf = reader.GetFieldValue<byte[]>(1);
        }
        var browser = context.RequestServices.GetRequiredService<WebBrowserState>().Read(context);
        if (browser is null || !CryptographicOperations.FixedTimeEquals(csrf, WebConfiguration.Hash(browser.CsrfToken)))
            throw new WebRequestException(401, "invalid_session");
        if (!bootstrap && (context.Request.Headers[WebConfiguration.FamilyHeader].Count != 1 ||
            context.Request.Headers[WebConfiguration.FamilyHeader][0] != session.FamilyId.ToString("D")))
            throw new WebRequestException(409, "session_changed");

        // Reject a body before consuming the refresh token. Explicit refresh is one-shot.
        if (rotate) await AccountBodyReader.Empty(context);
        if (rotate || session.AccessExpiresAt <= clock.GetUtcNow().AddMinutes(1))
        {
            // Refresh and persistence share the native transaction, so a crash cannot lose
            // a successfully consumed refresh token between the two writes.
            session = await accounts.RefreshBrowserSessionAsync(session.RefreshToken, hash,
                next => protector.Protect(JsonSerializer.Serialize(next, AccountJson.CreateOptions())), ct);
        }
        await accounts.AuthenticateAsync(session.AccessToken, ct);
        var result = await operation(session.AccessToken);
        await tx.CommitAsync(ct);
        return result;
    }

    internal async Task DeleteAsync(HttpContext context)
    {
        var id = context.Request.Cookies[WebConfiguration.SessionCookie];
        if (WebConfiguration.Token(id))
        {
            await using var connection = source.CreateConnection();
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand($"DELETE FROM {schema}.web_sessions WHERE session_hash=@id", connection);
            command.Parameters.AddWithValue("id", WebConfiguration.Hash(id!));
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }
        context.Response.Cookies.Delete(WebConfiguration.SessionCookie, WebConfiguration.Cookie(clock.GetUtcNow().AddDays(-1)));
    }
}
