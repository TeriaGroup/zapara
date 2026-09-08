using System.Text.Json.Nodes;

namespace Zapara.Server.Accounts;

public sealed class AccountLifecycleParticipant(AccountsConfiguration configuration) : IAccountLifecycleParticipant
{
    private readonly string schema = configuration.QuotedSchema;
    public string Module => "accounts";

    public async Task ContributeExportAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        JsonObject profile;
        await using (var command = context.Command($"""
            SELECT u.user_id,u.username,u.display_name,u.created_at,r.email
            FROM {schema}.users u
            LEFT JOIN {schema}.recovery_addresses r ON r.user_id=u.user_id
            WHERE u.user_id=@p0
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new AccountServiceException(AccountFailure.InvalidSession);
            profile = new JsonObject
            {
                ["userId"] = AccountExportDocument.Id(reader.GetGuid(0)),
                ["username"] = reader.GetString(1),
                ["displayName"] = reader.IsDBNull(2) ? null : JsonValue.Create(reader.GetString(2)),
                ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(3)),
                ["recoveryEmail"] = reader.IsDBNull(4) ? null : JsonValue.Create(reader.GetString(4))
            };
        }
        context.Export.SetProfile(profile);
        await using (var command = context.Command($"""
            SELECT provider,subject,linked_at FROM {schema}.external_identities WHERE user_id=@p0 ORDER BY provider
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.LinkedProviders.Add(new JsonObject
                {
                    ["provider"] = reader.GetString(0),
                    ["subject"] = reader.GetString(1),
                    ["linkedAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(2))
                });
        }
        await using (var command = context.Command($"""
            SELECT family_id,device_id,device_name,platform,created_at,last_seen_at,expires_at,revoked_at
            FROM {schema}.session_families WHERE user_id=@p0 ORDER BY created_at, family_id
            """, context.UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                context.Export.Devices.Add(new JsonObject
                {
                    ["familyId"] = AccountExportDocument.Id(reader.GetGuid(0)),
                    ["deviceId"] = AccountExportDocument.Id(reader.GetGuid(1)),
                    ["deviceName"] = reader.GetString(2),
                    ["platform"] = reader.GetString(3),
                    ["createdAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(4)),
                    ["lastSeenAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(5)),
                    ["expiresAt"] = AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(6)),
                    ["revokedAt"] = reader.IsDBNull(7) ? null : AccountExportDocument.Utc(reader.GetFieldValue<DateTimeOffset>(7))
                });
        }
    }

    public async Task DeleteOwnedDataAsync(AccountLifecycleContext context, CancellationToken ct)
    {
        var user = context.UserId;
        var now = context.UtcNow;
        var placeholder = user.ToString("N");
        string original;
        await using (var command = context.Command($"SELECT normalized_username FROM {schema}.users WHERE user_id=@p0", user))
            original = (string)(await command.ExecuteScalarAsync(ct) ?? placeholder);
        await context.ExecuteAsync($"DELETE FROM {schema}.export_jobs WHERE user_id=@p0", user);
        await context.ExecuteAsync($"""
            DELETE FROM {schema}.oauth_transactions WHERE initiator_user_id=@p0 OR resolved_user_id=@p0 OR owner_id=@p0
            """, user);
        await context.ExecuteAsync($"DELETE FROM {schema}.reauth_proofs WHERE user_id=@p0", user);
        await context.ExecuteAsync($"""
            DELETE FROM {schema}.access_tokens WHERE family_id IN (SELECT family_id FROM {schema}.session_families WHERE user_id=@p0)
            """, user);
        await context.ExecuteAsync($"""
            DELETE FROM {schema}.refresh_tokens WHERE family_id IN (SELECT family_id FROM {schema}.session_families WHERE user_id=@p0)
            """, user);
        await context.ExecuteAsync($"DELETE FROM {schema}.recovery_email_tokens WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.password_reset_tokens WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.recovery_addresses WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.password_credentials WHERE user_id=@p0", user);
        await context.ExecuteAsync($"DELETE FROM {schema}.external_identities WHERE user_id=@p0", user);
        await context.ExecuteAsync($"""
            UPDATE {schema}.users SET status='deleting', display_name=NULL, username=@p0, normalized_username=@p0
            WHERE user_id=@p1
            """, placeholder, user);
        await context.ExecuteAsync($"""
            INSERT INTO {schema}.deletion_manifests(user_id,normalized_username,deleted_at)
            VALUES(@p0,@p1,@p2) ON CONFLICT (user_id) DO NOTHING
            """, user, original, now);
    }
}
