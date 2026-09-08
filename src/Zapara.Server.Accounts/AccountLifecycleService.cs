using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;

namespace Zapara.Server.Accounts;

public sealed class AccountLifecycleService(
    AccountsDataSource dataSource,
    AccountsConfiguration configuration,
    TimeProvider clock,
    IEnumerable<IAccountLifecycleParticipant> participants,
    ILogger<AccountLifecycleService> logger)
{
    private readonly string schema = configuration.QuotedSchema;
    private readonly IAccountLifecycleParticipant[] modules = participants.ToArray();

    public Task<ExportJobResponse> CreateExportAsync(string accessToken, ProofRequest request, CancellationToken ct = default)
    {
        Required(request);
        if (string.IsNullOrWhiteSpace(request.ProofToken)) throw new AccountServiceException(AccountFailure.InvalidRequest);
        var access = AccountTokens.Hash(accessToken, "za_");
        var proof = ProofHash(request.ProofToken);
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, family) = await db.AuthorizeAsync(access);
            await ConsumeProofAsync(db, proof, user, family, "export", ct);
            var exportId = Guid.NewGuid();
            var now = db.Now;
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.export_jobs(export_id,user_id,status,created_at)
                VALUES(@p0,@p1,'queued',@p2)
                """, exportId, user.User.UserId, now);
            var document = new AccountExportDocument(now);
            var context = new AccountLifecycleContext(user.User.UserId, now, db.Connection, tx, document, ct);
            foreach (var module in modules)
                await module.ContributeExportAsync(context, ct);
            var payload = JsonSerializer.SerializeToUtf8Bytes(document.Root, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var expires = now.AddHours(24);
            await db.ExecuteAsync($"""
                UPDATE {schema}.export_jobs
                SET status='ready', completed_at=@p0, expires_at=@p1, payload=@p2, byte_size=@p3
                WHERE export_id=@p4
                """, now, expires, payload, payload.Length, exportId);
            await db.AuditAsync(user.User.UserId, family.Id, "export");
            await db.CommitAsync(tx);
            return new ExportJobResponse(exportId, "ready", now, now, expires);
        }, ct);
    }

    public Task<ExportJobResponse> GetExportAsync(string accessToken, Guid exportId, CancellationToken ct = default)
    {
        AccountValidation.Id(exportId);
        var access = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, _) = await db.AuthorizeAsync(access);
            var job = await ReadJobAsync(db, exportId, user.User.UserId, ct);
            await db.CommitAsync(tx);
            return job;
        }, ct);
    }

    public Task<(byte[] Payload, string FileName)> DownloadAsync(string accessToken, Guid exportId, CancellationToken ct = default)
    {
        AccountValidation.Id(exportId);
        var access = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, _) = await db.AuthorizeAsync(access);
            byte[] payload;
            await using (var command = db.Command($"""
                SELECT payload,status,expires_at FROM {schema}.export_jobs
                WHERE export_id=@p0 AND user_id=@p1 FOR UPDATE
                """, exportId, user.User.UserId))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct) || reader.GetString(1) != "ready" ||
                    reader.GetFieldValue<DateTimeOffset>(2) <= db.Now || reader.IsDBNull(0))
                    throw new AccountServiceException(AccountFailure.ExportNotFound);
                payload = reader.GetFieldValue<byte[]>(0);
            }
            await db.CommitAsync(tx);
            return (payload, "zapara-export-" + exportId.ToString("D") + ".json");
        }, ct);
    }

    public async Task<DeleteAccountResponse> DeleteAccountAsync(string accessToken, ProofRequest request, CancellationToken ct = default)
    {
        Required(request);
        if (string.IsNullOrWhiteSpace(request.ProofToken)) throw new AccountServiceException(AccountFailure.InvalidRequest);
        var access = AccountTokens.Hash(accessToken, "za_");
        var proof = ProofHash(request.ProofToken);
        await DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, family) = await db.AuthorizeAsync(access);
            await ConsumeProofAsync(db, proof, user, family, "delete_account", ct);
            await db.ExecuteAsync($"UPDATE {schema}.users SET status='deleting' WHERE user_id=@p0", user.User.UserId);
            await db.RevokeAsync(user.User.UserId, null, "deleting");
            await db.ExecuteAsync($"""
                INSERT INTO {schema}.deletion_jobs(job_id,user_id,status,created_at)
                VALUES(@p0,@p1,'queued',@p2) ON CONFLICT (user_id) DO NOTHING
                """, Guid.NewGuid(), user.User.UserId, db.Now);
            await db.AuditAsync(user.User.UserId, family.Id, "delete_account");
            await db.CommitAsync(tx);
            return true;
        }, ct);
        try { await ProcessPendingDeletionsAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Фоновая очистка аккаунта недоступна.");
        }
        return new DeleteAccountResponse("deleting", false);
    }

    public Task ProcessPendingDeletionsAsync(CancellationToken ct = default)
        => DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var jobs = new List<(Guid JobId, Guid UserId)>();
            await using (var command = db.Command($"""
                SELECT job_id,user_id FROM {schema}.deletion_jobs
                WHERE status IN ('queued','running') ORDER BY created_at, job_id FOR UPDATE
                """))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    jobs.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
            foreach (var job in jobs)
                await ApplyDeletionAsync(db, tx, job.JobId, job.UserId, ct);
            await db.CommitAsync(tx);
            return true;
        }, ct);

    public Task ReplayDeletionManifestsAsync(CancellationToken ct = default)
        => DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var users = new List<Guid>();
            await using (var command = db.Command($"SELECT user_id FROM {schema}.deletion_manifests ORDER BY deleted_at, user_id"))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    users.Add(reader.GetGuid(0));
            }
            foreach (var userId in users)
                await ApplyOwnedDataAsync(db, tx, userId, ct);
            await db.CommitAsync(tx);
            return true;
        }, ct);

    private async Task ApplyDeletionAsync(AccountRepository db, NpgsqlTransaction tx, Guid jobId, Guid userId, CancellationToken ct)
    {
        await db.ExecuteAsync($"UPDATE {schema}.deletion_jobs SET status='running' WHERE job_id=@p0 AND status IN ('queued','running')", jobId);
        await ApplyOwnedDataAsync(db, tx, userId, ct);
        await db.ExecuteAsync($"""
            UPDATE {schema}.deletion_jobs SET status='completed', completed_at=@p0 WHERE job_id=@p1
            """, db.Now, jobId);
    }

    private async Task ApplyOwnedDataAsync(AccountRepository db, NpgsqlTransaction tx, Guid userId, CancellationToken ct)
    {
        var document = new AccountExportDocument(db.Now);
        var context = new AccountLifecycleContext(userId, db.Now, db.Connection, tx, document, ct);
        foreach (var module in modules.Reverse())
            await module.DeleteOwnedDataAsync(context, ct);
    }

    private async Task<ExportJobResponse> ReadJobAsync(AccountRepository db, Guid exportId, Guid userId, CancellationToken ct)
    {
        await using var command = db.Command($"""
            SELECT export_id,status,created_at,completed_at,expires_at
            FROM {schema}.export_jobs WHERE export_id=@p0 AND user_id=@p1
            """, exportId, userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new AccountServiceException(AccountFailure.ExportNotFound);
        var status = reader.GetString(1);
        var created = reader.GetFieldValue<DateTimeOffset>(2);
        DateTimeOffset? completed = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
        DateTimeOffset? expires = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4);
        if (status == "ready" && expires is { } expiry && expiry <= db.Now)
            throw new AccountServiceException(AccountFailure.ExportNotFound);
        return new ExportJobResponse(reader.GetGuid(0), status, created, completed, expires);
    }

    private async Task ConsumeProofAsync(AccountRepository db, byte[] hash, AccountRow user, FamilyRow family,
        string purpose, CancellationToken ct)
    {
        await using var command = db.Command($"""
            SELECT user_id,family_id,purpose,security_version,expires_at,consumed_at,reservation
            FROM {schema}.reauth_proofs WHERE proof_hash=@p0 FOR UPDATE
            """, hash);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetGuid(0) != user.User.UserId || reader.GetGuid(1) != family.Id ||
            reader.GetString(2) != purpose || reader.GetInt64(3) != user.Version ||
            reader.GetFieldValue<DateTimeOffset>(4) <= db.Now || !reader.IsDBNull(5) || !reader.IsDBNull(6))
            throw ExternalAuthException.Invalid();
        await reader.DisposeAsync();
        await db.ExecuteAsync($"UPDATE {schema}.reauth_proofs SET consumed_at=@p0 WHERE proof_hash=@p1", db.Now, hash);
    }

    private static byte[] ProofHash(string proof)
    {
        ExternalSecrets.Token(proof, 43, 43);
        return ExternalSecrets.Hash(proof);
    }

    private static void Required(object? request)
    {
        if (request is null) throw new AccountServiceException(AccountFailure.InvalidRequest);
    }

    private async Task<T> DatabaseAsync<T>(Func<AccountRepository, Task<T>> operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var connection = dataSource.CreateConnection();
            await connection.OpenAsync(ct);
            return await operation(new(connection, schema, clock, ct));
        }
        catch (ArgumentException)
        {
            throw new AccountServiceException(AccountFailure.InvalidRequest);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            throw new AccountServiceException(AccountFailure.DbUnavailable);
        }
    }
}
