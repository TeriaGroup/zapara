using System.Text.RegularExpressions;
using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

public sealed class QuotaReservation
{
    private readonly QuotaLedger ledger;
    private readonly SemaphoreSlim releaseGate = new(1, 1);
    private bool released;
    internal Guid User { get; }
    internal string? Group { get; }
    internal long Bytes { get; }
    internal QuotaReservation(QuotaLedger ledger, Guid user, string? group, long bytes)
        => (this.ledger, User, Group, Bytes) = (ledger, user, group, bytes);

    public async Task ReleaseAsync(CancellationToken ct)
    {
        await releaseGate.WaitAsync(ct);
        try
        {
            if (released) return;
            await ledger.ReleaseAsync(this, ct);
            released = true;
        }
        finally { releaseGate.Release(); }
    }
}

public sealed class QuotaLedger(AccountsDataSource data, Microsoft.Extensions.Configuration.IConfiguration configuration)
{
    private static readonly Regex SchemaName = new(@"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant);
    private string Schema => OperatorSettings.Schema(configuration);

    public async Task Reserve(IAccountUnitOfWork accounts, string token, long bytes, string? groupId, CancellationToken ct)
        => _ = await ReserveTrackedAsync(accounts, token, bytes, groupId, ct);

    internal async Task<QuotaReservation> ReserveTrackedAsync(IAccountUnitOfWork accounts, string token, long bytes, string? groupId, CancellationToken ct)
    {
        if (bytes < 0) throw new SocialException(400, "invalid_request");
        try
        {
            return await accounts.ExecuteAsync(token, async (context, cancellation) =>
            {
                var connection = context.Connection;
                var tx = context.Transaction;
                await EnsureAsync(connection, tx, cancellation);
                var group = StudyGroupScope.Choose(groupId, await GroupsAsync(connection, tx, context.UserId, cancellation));
                var limits = await LimitsAsync(connection, tx, cancellation);
                // All writers lock user first, then the one group; limit checks and both
                // increments commit in the same authenticated account transaction.
                var userUsed = await LockAsync(connection, tx, "user", context.UserId.ToString("D"), cancellation);
                var groupUsed = group is null ? 0 : await LockAsync(connection, tx, "group", group, cancellation);
                var decision = QuotaRules.Decide(new(userUsed, limits.User, groupUsed, limits.Group, group is not null), bytes);
                if (!decision.Allowed) throw new SocialException(413, decision.Code!);
                await ChangeAsync(connection, tx, context.UserId, group, bytes, cancellation);
                return new QuotaReservation(this, context.UserId, group, bytes);
            }, ct);
        }
        catch (AccountServiceException error) when (error.Failure == AccountFailure.DbUnavailable)
        { throw new SocialException(503, "storage_unavailable"); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        { throw new SocialException(503, "storage_unavailable"); }
    }

    public async Task Release(IAccountUnitOfWork accounts, string token, long bytes, string? groupId, CancellationToken ct)
    {
        if (bytes < 0) throw new SocialException(400, "invalid_request");
        await accounts.ExecuteAsync(token, async (context, cancellation) =>
        {
            await EnsureAsync(context.Connection, context.Transaction, cancellation);
            var group = StudyGroupScope.Choose(groupId, await GroupsAsync(context.Connection, context.Transaction, context.UserId, cancellation));
            await ChangeAsync(context.Connection, context.Transaction, context.UserId, group, -bytes, cancellation);
            return true;
        }, ct);
    }

    internal async Task ReleaseAsync(QuotaReservation reservation, CancellationToken ct)
    {
        await using var connection = data.CreateConnection();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await ChangeAsync(connection, tx, reservation.User, reservation.Group, -reservation.Bytes, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReleaseFailedAsync(QuotaReservation reservation)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await reservation.ReleaseAsync(cleanup.Token); }
        catch (Exception) { /* Preserve the original failure; an unavailable ledger stays conservative. */ }
    }

    public (long UserUsed, long UserLimit, long GroupUsed, long GroupLimit) Usage(Guid userId, string? groupId)
        => UsageAsync(userId, groupId).GetAwaiter().GetResult();

    private async Task<(long UserUsed, long UserLimit, long GroupUsed, long GroupLimit)> UsageAsync(Guid userId, string? groupId)
    {
        await using var connection = data.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await EnsureAsync(connection, tx, CancellationToken.None);
        var group = StudyGroupScope.Choose(groupId, await GroupsAsync(connection, tx, userId, CancellationToken.None));
        var limits = await LimitsAsync(connection, tx, CancellationToken.None);
        var user = await LockAsync(connection, tx, "user", userId.ToString("D"), CancellationToken.None);
        var groupUsed = group is null ? 0 : await LockAsync(connection, tx, "group", group, CancellationToken.None);
        await tx.CommitAsync();
        return (user, limits.User, groupUsed, limits.Group);
    }

    private async Task EnsureAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (await PresentAsync(connection, tx, Schema + ".quota_counters", ct)) return;
        await using var command = Command(connection, tx, $"""
            SELECT pg_advisory_xact_lock(hashtextextended(@name, 0));
            CREATE SCHEMA IF NOT EXISTS {Schema};
            CREATE TABLE IF NOT EXISTS {Schema}.quota_counters (scope text NOT NULL, scope_id text NOT NULL, bytes bigint NOT NULL, PRIMARY KEY (scope, scope_id))
            """, ("name", Schema + ".quota_counters"));
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<string>> GroupsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid user, CancellationToken ct)
    {
        var schema = configuration["Communities:Schema"];
        if (string.IsNullOrWhiteSpace(schema) || !SchemaName.IsMatch(schema)) return [];
        if (!await PresentAsync(connection, tx, schema + ".memberships", ct)) return [];
        await using var command = Command(connection, tx, $"""
            SELECT COALESCE(map.group_id, mem.community_id::text)
            FROM {schema}.memberships mem
            LEFT JOIN {schema}.catalog_maps map ON map.community_id=mem.community_id
            WHERE mem.user_id=@user AND mem.status='active' ORDER BY mem.created_at,mem.community_id
            """, ("user", user));
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var group = reader.GetString(0);
            if (group.Length is > 0 and <= 64) result.Add(group);
        }
        return result;
    }

    private async Task<(long User, long Group)> LimitsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        var user = QuotaRules.DefaultUserBytes;
        var group = QuotaRules.DefaultGroupBytes;
        if (!await PresentAsync(connection, tx, Schema + ".system_settings", ct)) return (user, group);
        await using var command = Command(connection, tx, $"SELECT key,value FROM {Schema}.system_settings WHERE key IN ('quota_user_bytes','quota_group_bytes')");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!long.TryParse(reader.GetString(1), out var value) || value <= 0) continue;
            if (reader.GetString(0) == "quota_user_bytes") user = value;
            else group = value;
        }
        return (user, group);
    }

    private async Task<long> LockAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string scope, string id, CancellationToken ct)
    {
        await using var command = Command(connection, tx, $"""
            INSERT INTO {Schema}.quota_counters(scope,scope_id,bytes) VALUES(@scope,@id,0) ON CONFLICT DO NOTHING;
            SELECT bytes FROM {Schema}.quota_counters WHERE scope=@scope AND scope_id=@id FOR UPDATE
            """, ("scope", scope), ("id", id));
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task ChangeAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid user, string? group, long bytes, CancellationToken ct)
    {
        await Bump("user", user.ToString("D"));
        if (group is not null) await Bump("group", group);
        async Task Bump(string scope, string id)
        {
            await using var command = Command(connection, tx, $"""
                UPDATE {Schema}.quota_counters SET bytes=GREATEST(bytes+@bytes,0) WHERE scope=@scope AND scope_id=@id
                """, ("bytes", bytes), ("scope", scope), ("id", id));
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<bool> PresentAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string name, CancellationToken ct)
    {
        await using var command = Command(connection, tx, "SELECT to_regclass(@name)::text", ("name", name));
        return await command.ExecuteScalarAsync(ct) is string;
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
