using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

// The host bridges this to the community module, preserving its permission rules and transaction.
public interface IAvatarCommunityAccess
{
    Task<string> RequireAsync(TrustedAccountContext context, Guid communityId, bool write, CancellationToken ct);
    Task<bool> ShareMembershipAsync(TrustedAccountContext context, Guid userId, CancellationToken ct);
}

public sealed record AvatarRevision(string Revision);
public sealed record AvatarDownload(Stream Stream, string Revision);

public interface IAvatarService
{
    Task<AvatarDownload> OpenUserAsync(string token, Guid userId, CancellationToken ct = default);
    Task<AvatarDownload> OpenGroupAsync(string token, Guid communityId, CancellationToken ct = default);
    Task<AvatarRevision> PutAsync(string token, Guid? communityId, byte[] input, CancellationToken ct = default);
    Task DeleteAsync(string token, Guid? communityId, CancellationToken ct = default);
}

public sealed class AvatarService(IAccountUnitOfWork accounts, SocialConfiguration configuration,
    MediaStore media, StudentUpload uploads, QuotaLedger ledger, IAvatarCommunityAccess? communities = null) : IAvatarService
{
    public async Task<AvatarDownload> OpenUserAsync(string token, Guid userId, CancellationToken ct = default)
    {
        var avatar = await accounts.ExecuteAsync(token, async (context, cancellation) =>
        {
            if (context.UserId != userId && !await IsFriendAsync(context, userId, cancellation)
                && (communities is null || !await communities.ShareMembershipAsync(context, userId, cancellation)))
                throw new SocialException(404, "not_found");
            return await ReadAsync(context, "user_avatars", userId, cancellation);
        }, ct);
        return new(media.Open(avatar.StoredName), avatar.Revision.ToString("D"));
    }

    public async Task<AvatarDownload> OpenGroupAsync(string token, Guid communityId, CancellationToken ct = default)
    {
        var avatar = await accounts.ExecuteAsync(token, async (context, cancellation) =>
        {
            await RequireGroupAsync(context, communityId, false, cancellation);
            return await ReadAsync(context, "group_avatars", communityId, cancellation);
        }, ct);
        return new(media.Open(avatar.StoredName), avatar.Revision.ToString("D"));
    }

    public async Task<AvatarRevision> PutAsync(string token, Guid? communityId, byte[] input, CancellationToken ct = default)
    {
        // Check access before spending work on image decoding or reserving upload traffic.
        var quotaGroup = await accounts.ExecuteAsync(token, async (context, cancellation) =>
        {
            return communityId is { } group ? await RequireGroupAsync(context, group, true, cancellation) : null;
        }, ct);
        var bytes = AvatarCompressor.Compress(input);
        var revision = Guid.NewGuid();
        var stored = "avatar-" + revision.ToString("N") + ".webp";
        var accepted = await uploads.AcceptTrackedAsync(accounts, token, quotaGroup, stored, bytes, ct);
        try
        {
            await accounts.ExecuteAsync(token, async (context, cancellation) =>
            {
                if (communityId is { } group) await RequireGroupAsync(context, group, true, cancellation);
                var table = communityId.HasValue ? "group_avatars" : "user_avatars";
                var owner = communityId ?? context.UserId;
                await QueueCurrentAsync(context, table, owner, cancellation);
                await using var command = Command(context, $"""
                    INSERT INTO {configuration.QuotedSchema}.{table}(owner_id,stored_name,revision,updated_at)
                    VALUES(@p0,@p1,@p2,@p3)
                    ON CONFLICT(owner_id) DO UPDATE SET stored_name=EXCLUDED.stored_name,revision=EXCLUDED.revision,updated_at=EXCLUDED.updated_at
                    """, owner, stored, revision, context.UtcNow);
                await command.ExecuteNonQueryAsync(cancellation);
                return true;
            }, ct);
        }
        catch
        {
            try { media.Delete(stored); }
            catch (Exception)
            {
                // Keep a durable retry when the store is temporarily unavailable.
                try
                {
                    await accounts.ExecuteAsync(token, async (context, cancellation) =>
                    {
                        await using var command = Command(context, $"INSERT INTO {configuration.QuotedSchema}.file_purge(stored_name,created_at) VALUES(@p0,@p1) ON CONFLICT DO NOTHING", stored, context.UtcNow);
                        await command.ExecuteNonQueryAsync(cancellation);
                        return true;
                    }, CancellationToken.None);
                }
                catch (Exception) { /* Preserve the original failure if the account/database is unavailable too. */ }
            }
            await ledger.ReleaseFailedAsync(accepted.Reservation);
            throw;
        }
        return new(revision.ToString("D"));
    }

    public Task DeleteAsync(string token, Guid? communityId, CancellationToken ct = default)
        => accounts.ExecuteAsync(token, async (context, cancellation) =>
        {
            if (communityId is { } group) await RequireGroupAsync(context, group, true, cancellation);
            var table = communityId.HasValue ? "group_avatars" : "user_avatars";
            var owner = communityId ?? context.UserId;
            await QueueCurrentAsync(context, table, owner, cancellation);
            await using var command = Command(context, $"DELETE FROM {configuration.QuotedSchema}.{table} WHERE owner_id=@p0", owner);
            await command.ExecuteNonQueryAsync(cancellation);
            return true;
        }, ct);

    private Task<string> RequireGroupAsync(TrustedAccountContext context, Guid id, bool write, CancellationToken ct)
        => communities?.RequireAsync(context, id, write, ct) ?? throw new SocialException(404, "not_found");

    private async Task<bool> IsFriendAsync(TrustedAccountContext context, Guid userId, CancellationToken ct)
    {
        await using var command = Command(context, $"""
            SELECT 1 FROM {configuration.QuotedSchema}.friendships
            WHERE status='accepted' AND ((requester_id=@p0 AND addressee_id=@p1) OR (requester_id=@p1 AND addressee_id=@p0))
            """, context.UserId, userId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<(string StoredName, Guid Revision)> ReadAsync(TrustedAccountContext context, string table, Guid owner, CancellationToken ct)
    {
        await using var command = Command(context, $"SELECT stored_name,revision FROM {configuration.QuotedSchema}.{table} WHERE owner_id=@p0", owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new SocialException(404, "not_found");
        return (reader.GetString(0), reader.GetGuid(1));
    }

    private async Task QueueCurrentAsync(TrustedAccountContext context, string table, Guid owner, CancellationToken ct)
    {
        await using var command = Command(context, $"""
            INSERT INTO {configuration.QuotedSchema}.file_purge(stored_name,created_at)
            SELECT stored_name,@p1 FROM {configuration.QuotedSchema}.{table} WHERE owner_id=@p0
            ON CONFLICT DO NOTHING
            """, owner, context.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand Command(TrustedAccountContext context, string sql, params object[] parameters)
    {
        var command = new NpgsqlCommand(sql, context.Connection, context.Transaction);
        for (var index = 0; index < parameters.Length; index++) command.Parameters.AddWithValue("p" + index, parameters[index]);
        return command;
    }
}
