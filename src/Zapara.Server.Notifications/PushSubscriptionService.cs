using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zapara.Server.Accounts;
using Zapara.Server.Sync;

namespace Zapara.Server.Notifications;

public sealed class PushSubscriptionService(AccountsDataSource source, AccountsConfiguration accountsConfiguration,
    AccountService accounts, [FromKeyedServices("Zapara.Web")] IDataProtectionProvider protection,
    PushConfiguration configuration, IPushTransport transport, TimeProvider clock, SyncConfiguration? syncConfiguration = null)
{
    private readonly string schema = accountsConfiguration.QuotedSchema;
    private readonly string? syncSchema = syncConfiguration?.QuotedSchema;
    private readonly IDataProtector protector = protection.CreateProtector("Zapara.Web.PushSubscriptions.v1");

    public Task<PushSubscriptionResponse> UpsertAsync(string accessToken, PushSubscriptionRequest request, CancellationToken ct)
    {
        if (request.Enabled) Available();
        PushValidation.Subscription(request);
        return accounts.ExecuteAsync<PushSubscriptionResponse>(accessToken, async (context, token) =>
        {
            var id = Guid.NewGuid();
            if (!request.Enabled)
            {
                await using var disable = Command(context.Connection, context.Transaction,
                    $"DELETE FROM {schema}.web_push_subscriptions WHERE family_id=@p0 RETURNING subscription_id", context.FamilyId);
                if (await disable.ExecuteScalarAsync(token) is Guid previous) id = previous;
                return new(id, false, request.TimeZone, await Times(context.Connection, context.Transaction, context.UserId, token));
            }
            await using var command = Command(context.Connection, context.Transaction, $"""
                INSERT INTO {schema}.web_push_subscriptions(subscription_id,family_id,endpoint_hash,protected_subscription,enabled,time_zone,created_at,updated_at)
                VALUES(@p0,@p1,@p2,@p3,true,@p4,@p5,@p5)
                ON CONFLICT (family_id) DO UPDATE SET endpoint_hash=EXCLUDED.endpoint_hash,protected_subscription=EXCLUDED.protected_subscription,
                  enabled=true,time_zone=EXCLUDED.time_zone,updated_at=EXCLUDED.updated_at
                RETURNING subscription_id
                """, id, context.FamilyId, SHA256.HashData(Encoding.UTF8.GetBytes(request.Endpoint)),
                protector.Protect(JsonSerializer.Serialize(request)), request.TimeZone, context.UtcNow);
            try { id = (Guid)(await command.ExecuteScalarAsync(token))!; }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            { throw new PushOperationException(409, "subscription_in_use"); }
            return new(id, true, request.TimeZone, await Times(context.Connection, context.Transaction, context.UserId, token));
        }, ct);
    }

    public Task<IReadOnlyList<PushSubscriptionResponse>> ListAsync(string accessToken, CancellationToken ct)
    {
        return accounts.ExecuteAsync<IReadOnlyList<PushSubscriptionResponse>>(accessToken, async (context, token) =>
        {
            var times = await Times(context.Connection, context.Transaction, context.UserId, token);
            await using var command = Command(context.Connection, context.Transaction,
                $"SELECT subscription_id,enabled,time_zone FROM {schema}.web_push_subscriptions WHERE family_id=@p0", context.FamilyId);
            await using var reader = await command.ExecuteReaderAsync(token);
            var result = new List<PushSubscriptionResponse>();
            while (await reader.ReadAsync(token)) result.Add(new(reader.GetGuid(0), reader.GetBoolean(1), reader.GetString(2), times));
            return result;
        }, ct);
    }

    public Task<bool> DeleteAsync(string accessToken, Guid id, CancellationToken ct)
        => accounts.ExecuteAsync(accessToken, async (context, token) =>
        {
            await using var command = Command(context.Connection, context.Transaction,
                $"DELETE FROM {schema}.web_push_subscriptions WHERE subscription_id=@p0 AND family_id=@p1", id, context.FamilyId);
            await command.ExecuteNonQueryAsync(token);
            return true;
        }, ct);

    public async Task<PushDeliveryOutcome> TestAsync(string accessToken, Guid id, CancellationToken ct)
    {
        Available();
        await accounts.ExecuteAsync(accessToken, async (context, token) =>
        {
            await using var command = Command(context.Connection, context.Transaction,
                $"SELECT 1 FROM {schema}.web_push_subscriptions WHERE subscription_id=@p0 AND family_id=@p1", id, context.FamilyId);
            if (await command.ExecuteScalarAsync(token) is null) throw new PushOperationException(404, "subscription_not_found");
            return true;
        }, ct);
        return await DispatchAsync(id, true, ct);
    }

    public async Task RunScheduledAsync(CancellationToken ct)
    {
        if (!configuration.Available) { await CleanupAsync(ct); return; }
        Guid after = Guid.Empty;
        while (true)
        {
            var page = new List<Guid>();
            await using (var connection = source.CreateConnection())
            {
                await connection.OpenAsync(ct);
                await using var command = Command(connection, null, $"SELECT subscription_id FROM {schema}.web_push_subscriptions WHERE subscription_id>@p0 ORDER BY subscription_id LIMIT 128", after);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) page.Add(reader.GetGuid(0));
            }
            foreach (var id in page) await DispatchAsync(id, false, ct);
            if (page.Count < 128) break;
            after = page[^1];
        }
        await CleanupAsync(ct);
    }

    private async Task<PushDeliveryOutcome> DispatchAsync(Guid id, bool test, CancellationToken ct)
    {
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(ct);
        Guid user;
        Guid family;
        string zone;
        await using (var command = Command(connection, null, $"""
            SELECT f.user_id,f.family_id,s.time_zone FROM {schema}.web_push_subscriptions s
            JOIN {schema}.session_families f ON f.family_id=s.family_id JOIN {schema}.users u ON u.user_id=f.user_id
            WHERE s.subscription_id=@p0 AND s.enabled AND f.revoked_at IS NULL AND f.expires_at>@p1 AND u.status='active'
            """, id, clock.GetUtcNow()))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return PushDeliveryOutcome.Gone;
            user = reader.GetGuid(0); family = reader.GetGuid(1); zone = reader.GetString(2);
        }
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone));
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var times = await Times(connection, null, user, ct);
        var slots = test ? new[] { 2 } : Enumerable.Range(0, 2).Where(slot => times[slot] == time).DistinctBy(slot => times[slot]).ToArray();
        var outcome = PushDeliveryOutcome.Unavailable;
        foreach (var slot in slots)
        {
            var claimTime = test ? clock.GetUtcNow().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : time;
            // Durable at-most-once claim commits before network I/O. A crash or ambiguous
            // provider response is never retried automatically, preventing duplicate alerts.
            await using (var claim = Command(connection, null, $"""
                INSERT INTO {schema}.web_push_deliveries(subscription_id,local_date,slot,scheduled_time,status,attempted_at)
                SELECT subscription_id,@p1,@p2,@p3,'claimed',@p4 FROM {schema}.web_push_subscriptions WHERE subscription_id=@p0
                ON CONFLICT DO NOTHING
                """, id, date, (short)slot, claimTime, clock.GetUtcNow()))
            {
                if (await claim.ExecuteNonQueryAsync(ct) != 1)
                {
                    await using var previous = Command(connection, null, $"SELECT status FROM {schema}.web_push_deliveries WHERE subscription_id=@p0 AND local_date=@p1 AND slot=@p2 AND scheduled_time=@p3", id, date, (short)slot, claimTime);
                    outcome = await previous.ExecuteScalarAsync(ct) is "accepted" ? PushDeliveryOutcome.Accepted : PushDeliveryOutcome.Unavailable;
                    continue;
                }
            }
            await using var tx = await connection.BeginTransactionAsync(ct);
            // Same user -> family lock order as Accounts. Revocation cannot commit before
            // this bounded in-flight send completes; the next send then observes revocation.
            await using (var lockUser = Command(connection, tx, $"SELECT 1 FROM {schema}.users WHERE user_id=@p0 AND status='active' FOR UPDATE", user))
                if (await lockUser.ExecuteScalarAsync(ct) is null) continue;
            await using (var lockFamily = Command(connection, tx, $"SELECT 1 FROM {schema}.session_families WHERE family_id=@p0 AND revoked_at IS NULL AND expires_at>@p1 FOR UPDATE", family, clock.GetUtcNow()))
                if (await lockFamily.ExecuteScalarAsync(ct) is null) continue;
            string protectedValue;
            await using (var subscription = Command(connection, tx, $"SELECT protected_subscription FROM {schema}.web_push_subscriptions WHERE subscription_id=@p0 AND enabled FOR UPDATE", id))
            {
                protectedValue = await subscription.ExecuteScalarAsync(ct) as string ?? "";
                if (protectedValue.Length == 0) continue;
            }
            if (!test && (await Times(connection, tx, user, ct))[slot] != time) continue;
            try
            {
                var target = JsonSerializer.Deserialize<PushSubscriptionRequest>(protector.Unprotect(protectedValue))!;
                outcome = await transport.SendAsync(target, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { outcome = PushDeliveryOutcome.Unavailable; }
            if (outcome == PushDeliveryOutcome.Gone)
            {
                await using var remove = Command(connection, tx, $"DELETE FROM {schema}.web_push_subscriptions WHERE subscription_id=@p0", id);
                await remove.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await using var update = Command(connection, tx, $"UPDATE {schema}.web_push_deliveries SET status=@p4 WHERE subscription_id=@p0 AND local_date=@p1 AND slot=@p2 AND scheduled_time=@p3", id, date, (short)slot, claimTime, outcome == PushDeliveryOutcome.Accepted ? "accepted" : "unavailable");
                await update.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        return outcome;
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = Command(connection, null, $"""
            DELETE FROM {schema}.web_push_subscriptions s USING {schema}.session_families f,{schema}.users u
                WHERE s.family_id=f.family_id AND f.user_id=u.user_id AND (f.revoked_at IS NOT NULL OR f.expires_at<=@p0 OR u.status<>'active');
            DELETE FROM {schema}.web_push_deliveries WHERE attempted_at<@p1
            """, clock.GetUtcNow(), clock.GetUtcNow().AddDays(-35));
        await command.ExecuteNonQueryAsync(ct);
    }
    private async Task<IReadOnlyList<string?>> Times(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid user, CancellationToken ct)
    {
        if (syncSchema is null) return [null, null];
        await using var command = Command(connection, transaction, $"SELECT payload->>'notifyTime1',payload->>'notifyTime2' FROM {syncSchema}.sync_records WHERE user_id=@p0 AND entity_type='settings' AND NOT tombstone", user);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? [reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)] : [null, null];
    }
    private void Available() { if (!configuration.Available) throw new PushOperationException(503, "push_unavailable"); }
    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        for (var index = 0; index < values.Length; index++) command.Parameters.AddWithValue("p" + index, values[index]);
        return command;
    }
}
