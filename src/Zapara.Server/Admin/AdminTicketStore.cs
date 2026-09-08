using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Admin;

public sealed class AdminTicketStore(AccountsDataSource dataSource, AdminConfiguration configuration, TimeProvider clock) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var version = long.Parse(ticket.Principal.FindFirstValue("cv")!);
        var now = clock.GetUtcNow().ToUniversalTime();
        var expires = ticket.Properties.ExpiresUtc?.ToUniversalTime() ?? now.Add(AdminDefaults.SessionLifetime);
        var sessionId = Guid.NewGuid();
        var token = AdminTokens.Create();
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand($"""
            INSERT INTO {configuration.QuotedSchema}.admin_sessions(
                session_id,token_hash,user_id,created_at,authenticated_at,expires_at,revoked_at,credential_version,reauth_until)
            VALUES(@id,@hash,@user,@now,@now,@expires,NULL,@cv,NULL)
            """, connection, tx))
        {
            insert.Parameters.AddWithValue("id", sessionId);
            insert.Parameters.AddWithValue("hash", AdminTokens.Hash(token));
            insert.Parameters.AddWithValue("user", userId);
            insert.Parameters.AddWithValue("now", now);
            insert.Parameters.AddWithValue("expires", expires);
            insert.Parameters.AddWithValue("cv", version);
            await insert.ExecuteNonQueryAsync();
        }
        await using (var audit = new NpgsqlCommand($"""
            INSERT INTO {configuration.QuotedSchema}.admin_audit(event_id,actor_id,action,object_type,object_id,outcome,created_at)
            VALUES(@id,@actor,'login','admin_session',@object,'success',@now)
            """, connection, tx))
        {
            audit.Parameters.AddWithValue("id", Guid.NewGuid());
            audit.Parameters.AddWithValue("actor", userId);
            audit.Parameters.AddWithValue("object", sessionId.ToString("D"));
            audit.Parameters.AddWithValue("now", now);
            await audit.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return token;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => Task.CompletedTask;

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        byte[] hash;
        try { hash = AdminTokens.Hash(key); }
        catch (AdminException) { return null; }
        var now = clock.GetUtcNow().ToUniversalTime();
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT s.session_id,s.user_id,s.expires_at,s.revoked_at,s.credential_version,u.status,u.credential_version
            FROM {configuration.QuotedSchema}.admin_sessions s
            JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=s.user_id
            JOIN {configuration.QuotedSchema}.platform_admins a ON a.user_id=s.user_id AND a.revoked_at IS NULL
            WHERE s.token_hash=@hash
            """, connection);
        command.Parameters.AddWithValue("hash", hash);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var sessionId = reader.GetGuid(0);
        var userId = reader.GetGuid(1);
        var expires = reader.GetFieldValue<DateTimeOffset>(2);
        var revoked = !reader.IsDBNull(3);
        var sessionVersion = reader.GetInt64(4);
        var status = reader.GetString(5);
        var credentialVersion = reader.GetInt64(6);
        if (revoked || now >= expires || status != "active" || sessionVersion != credentialVersion) return null;
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim("admin", "platform"),
            new Claim("sid", sessionId.ToString("D")),
            new Claim("cv", credentialVersion.ToString())
        ], AdminDefaults.Scheme);
        var properties = new AuthenticationProperties { ExpiresUtc = expires, IsPersistent = true };
        return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, AdminDefaults.Scheme);
    }

    public async Task RemoveAsync(string key)
    {
        byte[] hash;
        try { hash = AdminTokens.Hash(key); }
        catch (AdminException) { return; }
        var now = clock.GetUtcNow().ToUniversalTime();
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        Guid? sessionId = null;
        Guid? userId = null;
        await using (var select = new NpgsqlCommand($"""
            SELECT session_id,user_id FROM {configuration.QuotedSchema}.admin_sessions WHERE token_hash=@hash FOR UPDATE
            """, connection, tx))
        {
            select.Parameters.AddWithValue("hash", hash);
            await using var reader = await select.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                sessionId = reader.GetGuid(0);
                userId = reader.GetGuid(1);
            }
        }
        if (sessionId is null) { await tx.CommitAsync(); return; }
        await using (var revoke = new NpgsqlCommand($"""
            UPDATE {configuration.QuotedSchema}.admin_sessions SET revoked_at=@now WHERE session_id=@id AND revoked_at IS NULL
            """, connection, tx))
        {
            revoke.Parameters.AddWithValue("now", now);
            revoke.Parameters.AddWithValue("id", sessionId.Value);
            await revoke.ExecuteNonQueryAsync();
        }
        await using (var audit = new NpgsqlCommand($"""
            INSERT INTO {configuration.QuotedSchema}.admin_audit(event_id,actor_id,action,object_type,object_id,outcome,created_at)
            VALUES(@id,@actor,'logout','admin_session',@object,'success',@now)
            """, connection, tx))
        {
            audit.Parameters.AddWithValue("id", Guid.NewGuid());
            audit.Parameters.AddWithValue("actor", userId!.Value);
            audit.Parameters.AddWithValue("object", sessionId.Value.ToString("D"));
            audit.Parameters.AddWithValue("now", now);
            await audit.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
