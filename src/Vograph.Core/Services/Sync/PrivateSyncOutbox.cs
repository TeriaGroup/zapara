using Microsoft.Data.Sqlite;
using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public sealed class PrivateSyncOutboxEntry
{
    public Guid OpId { get; init; }
    public string EntityType { get; init; } = "";
    public Guid EntityId { get; init; }
    public long ExpectedRevision { get; init; }
    public string Action { get; init; } = "";
    public string Status { get; init; } = "";
    public long? LocalRowId { get; init; }
    public Guid? SyncEpoch { get; init; }
}

public sealed class PrivateSyncDraft
{
    public string EntityType { get; init; } = "";
    public Guid EntityId { get; init; }
    public Guid? OpId { get; init; }
}

public sealed class PrivateSyncOutbox
{
    private readonly SqliteConnection conn;
    public PrivateSyncOutbox(Database db, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(db);
        conn = db.Connection;
        Enabled = enabled;
    }

    public bool Enabled { get; }
    public Action? BeforeCommit { get; set; }
    public event Action? Changed;

    public T InTransaction<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Enabled) return action();
        var name = "o" + Guid.NewGuid().ToString("N");
        Exec($"SAVEPOINT {name}");
        try
        {
            var result = action();
            BeforeCommit?.Invoke();
            Exec($"RELEASE {name}");
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            try
            {
                Exec($"ROLLBACK TO {name}");
                Exec($"RELEASE {name}");
            }
            catch (SqliteException rollback)
            {
                throw new InvalidOperationException(rollback.Message, ex);
            }
            throw;
        }
    }

    public IReadOnlyList<PrivateSyncOutboxEntry> Pending()
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opId, entityType, entityId, expectedRevision, action, status, localRowId, syncEpoch FROM sync_outbox ORDER BY createdAtUtc, opId";
        using var r = cmd.ExecuteReader();
        var list = new List<PrivateSyncOutboxEntry>();
        while (r.Read()) list.Add(ReadEntry(r));
        return list;
    }

    public PrivateSyncOutboxEntry? Find(Guid opId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opId, entityType, entityId, expectedRevision, action, status, localRowId, syncEpoch FROM sync_outbox WHERE opId=@id";
        cmd.Parameters.AddWithValue("@id", opId.ToString("D"));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEntry(r) : null;
    }

    public IReadOnlyList<PrivateSyncDraft> Drafts()
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT entityType, entityId, opId FROM sync_draft";
        using var r = cmd.ExecuteReader();
        var list = new List<PrivateSyncDraft>();
        while (r.Read())
        {
            list.Add(new PrivateSyncDraft
            {
                EntityType = r.GetString(0),
                EntityId = Guid.Parse(r.GetString(1)),
                OpId = r.IsDBNull(2) || string.IsNullOrEmpty(r.GetString(2)) ? null : Guid.Parse(r.GetString(2))
            });
        }
        return list;
    }

    public Guid? SyncEpoch
    {
        get
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT syncEpoch FROM sync_state WHERE id=1";
            var value = cmd.ExecuteScalar() as string;
            return string.IsNullOrEmpty(value) ? null : Guid.Parse(value);
        }
    }

    public long AfterSequence
    {
        get
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT afterSequence FROM sync_state WHERE id=1";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    public void SetEpoch(Guid epoch, long afterSequence)
    {
        var previous = SyncEpoch;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_state SET syncEpoch=@e, afterSequence=@a WHERE id=1";
        cmd.Parameters.AddWithValue("@e", epoch.ToString("D"));
        cmd.Parameters.AddWithValue("@a", afterSequence);
        cmd.ExecuteNonQuery();
        if (previous != epoch) ClearRowEpochs();
    }

    public void ClearRowEpochs()
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_outbox SET syncEpoch=NULL WHERE status='pending'";
        cmd.ExecuteNonQuery();
    }

    public void CancelPending(string entityType, Guid entityId)
    {
        foreach (var row in Pending().Where(r => r.EntityType == entityType && r.EntityId == entityId && r.Status == "pending"))
            DeleteOp(row.OpId);
    }

    public void Enqueue(Guid opId, string entityType, Guid entityId, long expectedRevision, string action, SyncValue? value, long? localRowId)
    {
        if (!Enabled) return;
        if (Find(opId) is { } existing)
        {
            if (!SameIntent(existing, entityType, action, value))
                throw new InvalidOperationException("Повтор операции с другим содержимым.");
            return;
        }

        foreach (var row in Pending().Where(r => r.EntityType == entityType && r.EntityId == entityId && r.Status == "pending"))
        {
            if (action == "delete" && row.ExpectedRevision == 0 && row.Action == "upsert")
            {
                DeleteOp(row.OpId);
                return;
            }
            expectedRevision = row.ExpectedRevision;
            DeleteOp(row.OpId);
        }

        if (action == "delete" && expectedRevision == 0) return;

        var payload = value is null ? null : ValueBytes(value);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO sync_outbox (opId, entityType, entityId, expectedRevision, action, payload, localRowId, status, createdAtUtc)
VALUES (@op,@type,@id,@rev,@act,@p,@row,'pending',@ca)";
        cmd.Parameters.AddWithValue("@op", opId.ToString("D"));
        cmd.Parameters.AddWithValue("@type", entityType);
        cmd.Parameters.AddWithValue("@id", entityId.ToString("D"));
        cmd.Parameters.AddWithValue("@rev", expectedRevision);
        cmd.Parameters.AddWithValue("@act", action);
        cmd.Parameters.AddWithValue("@p", payload is null ? DBNull.Value : payload);
        cmd.Parameters.AddWithValue("@row", localRowId is null ? DBNull.Value : localRowId.Value);
        cmd.Parameters.AddWithValue("@ca", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
        Alias(entityType, localRowId, entityId);
    }

    public bool SameIntent(PrivateSyncOutboxEntry existing, string entityType, string action, SyncValue? value)
    {
        if (existing.EntityType != entityType || existing.Action != action) return false;
        var stored = ReadPayload(existing.OpId);
        if (action == "delete") return value is null && stored is null;
        if (value is null || stored is null) return false;
        if (stored.AsSpan().SequenceEqual(ValueBytes(value))) return true;
        return entityType switch
        {
            "homework" when value is HomeworkValue proposed =>
                EqualHomework(SyncJson.Parse<HomeworkValue>(stored), proposed),
            "override" when value is OverrideValue proposed =>
                EqualOverride(SyncJson.Parse<OverrideValue>(stored), proposed),
            "completion" when value is CompletionValue proposed =>
                SyncJson.Parse<CompletionValue>(stored) == proposed,
            "friend" when value is FriendValue proposed =>
                SyncJson.Parse<FriendValue>(stored) == proposed,
            "settings" when value is SettingsValue proposed =>
                SyncJson.Parse<SettingsValue>(stored) == proposed,
            _ => false
        };
    }

    public SyncMutation? BuildMutation(PrivateSyncOutboxEntry row, Guid epoch)
    {
        var storedEpoch = row.SyncEpoch ?? StampEpoch(row.OpId, epoch);
        var payload = ReadPayload(row.OpId);
        SyncValue? value = null;
        if (payload is not null)
        {
            value = row.EntityType switch
            {
                "homework" => SyncJson.Parse<HomeworkValue>(payload),
                "completion" => SyncJson.Parse<CompletionValue>(payload),
                "override" => SyncJson.Parse<OverrideValue>(payload),
                "friend" => SyncJson.Parse<FriendValue>(payload),
                "settings" => SyncJson.Parse<SettingsValue>(payload),
                _ => throw new InvalidOperationException("Некорректный запрос синхронизации.")
            };
        }
        return new SyncMutation(storedEpoch, row.OpId, row.EntityType, row.EntityId, row.ExpectedRevision, row.Action, value);
    }

    public void ApplyAck(PrivateSyncOutboxEntry row, SyncRecord record)
    {
        switch (row.EntityType)
        {
            case "homework":
                Exec($"UPDATE homework SET revision={record.Revision}, tombstone={(record.Tombstone ? 1 : 0)} WHERE entityUuid='{row.EntityId:D}'");
                break;
            case "completion":
                Exec($"UPDATE homework_completion SET revision={record.Revision}, tombstone={(record.Tombstone ? 1 : 0)} WHERE entityUuid='{row.EntityId:D}'");
                break;
            case "override":
                Exec($"UPDATE overrides SET revision={record.Revision}, tombstone={(record.Tombstone ? 1 : 0)} WHERE entityUuid='{row.EntityId:D}'");
                break;
            case "friend":
                Exec($"UPDATE friends SET revision={record.Revision}, tombstone={(record.Tombstone ? 1 : 0)} WHERE entityUuid='{row.EntityId:D}'");
                break;
            case "settings":
                Exec($"UPDATE settings SET revision={record.Revision}, tombstone={(record.Tombstone ? 1 : 0)} WHERE id=1");
                break;
        }
        DeleteOp(row.OpId);
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM sync_draft WHERE entityType=@t AND entityId=@id";
        del.Parameters.AddWithValue("@t", row.EntityType);
        del.Parameters.AddWithValue("@id", row.EntityId.ToString("D"));
        del.ExecuteNonQuery();
    }

    public void MarkConflict(PrivateSyncOutboxEntry row, SyncMutationResult? outcome)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE sync_outbox SET status='conflict' WHERE opId=@id";
            cmd.Parameters.AddWithValue("@id", row.OpId.ToString("D"));
            cmd.ExecuteNonQuery();
        }
        using var draft = conn.CreateCommand();
        draft.CommandText = @"INSERT INTO sync_draft (entityType, entityId, opId, localPayload, serverPayload, createdAtUtc)
VALUES (@t,@id,@op,@loc,@srv,@ca)
ON CONFLICT(entityType, entityId) DO UPDATE SET opId=excluded.opId, localPayload=excluded.localPayload, serverPayload=excluded.serverPayload, createdAtUtc=excluded.createdAtUtc";
        draft.Parameters.AddWithValue("@t", row.EntityType);
        draft.Parameters.AddWithValue("@id", row.EntityId.ToString("D"));
        draft.Parameters.AddWithValue("@op", row.OpId.ToString("D"));
        var local = ReadPayload(row.OpId);
        draft.Parameters.AddWithValue("@loc", local is null ? "{}" : System.Text.Encoding.UTF8.GetString(local));
        draft.Parameters.AddWithValue("@srv", outcome?.ServerRecord is { } rec ? System.Text.Encoding.UTF8.GetString(SyncJson.Serialize(rec)) : "{}");
        draft.Parameters.AddWithValue("@ca", DateTimeOffset.UtcNow.ToString("o"));
        draft.ExecuteNonQuery();
    }

    public bool HasPendingOrDraft(string entityType, Guid entityId)
        => Pending().Any(r => r.EntityType == entityType && r.EntityId == entityId)
           || Drafts().Any(d => d.EntityType == entityType && d.EntityId == entityId);

    public void TombstoneLocal(string entityType, Guid entityId, long revision)
    {
        var table = entityType switch
        {
            "homework" => "homework",
            "override" => "overrides",
            "friend" => "friends",
            "completion" => "homework_completion",
            _ => null
        };
        if (table is null) return;
        var key = table == "homework_completion" ? "entityUuid" : "entityUuid";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET tombstone=1, revision=@r WHERE {key}=@id";
        cmd.Parameters.AddWithValue("@r", revision);
        cmd.Parameters.AddWithValue("@id", entityId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    public void ApplyLive(SyncRecord record)
    {
        if (record.Tombstone)
        {
            TombstoneLocal(record.EntityType, record.EntityId, record.Revision);
            return;
        }
        // Identity-only revision bump when a live row already exists; value merge is the caller's job.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = record.EntityType switch
        {
            "homework" => "UPDATE homework SET revision=@r, tombstone=0 WHERE entityUuid=@id",
            "override" => "UPDATE overrides SET revision=@r, tombstone=0 WHERE entityUuid=@id",
            "friend" => "UPDATE friends SET revision=@r, tombstone=0 WHERE entityUuid=@id",
            "completion" => "UPDATE homework_completion SET revision=@r, tombstone=0 WHERE entityUuid=@id",
            "settings" => "UPDATE settings SET revision=@r, tombstone=0 WHERE id=1",
            _ => "SELECT 1"
        };
        cmd.Parameters.AddWithValue("@r", record.Revision);
        cmd.Parameters.AddWithValue("@id", record.EntityId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    public void Alias(string entityType, long? localId, Guid entityId)
    {
        if (localId is null) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO sync_alias (entityType, localId, entityUuid) VALUES (@t,@id,@u)";
        cmd.Parameters.AddWithValue("@t", entityType);
        cmd.Parameters.AddWithValue("@id", localId.Value);
        cmd.Parameters.AddWithValue("@u", entityId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private Guid StampEpoch(Guid opId, Guid epoch)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_outbox SET syncEpoch=@e WHERE opId=@id AND syncEpoch IS NULL";
        cmd.Parameters.AddWithValue("@e", epoch.ToString("D"));
        cmd.Parameters.AddWithValue("@id", opId.ToString("D"));
        cmd.ExecuteNonQuery();
        return Find(opId)?.SyncEpoch ?? epoch;
    }

    private void DeleteOp(Guid opId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sync_outbox WHERE opId=@id";
        cmd.Parameters.AddWithValue("@id", opId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private byte[]? ReadPayload(Guid opId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM sync_outbox WHERE opId=@id";
        cmd.Parameters.AddWithValue("@id", opId.ToString("D"));
        var value = cmd.ExecuteScalar();
        return value is byte[] bytes ? bytes : null;
    }

    private static PrivateSyncOutboxEntry ReadEntry(SqliteDataReader r) => new()
    {
        OpId = Guid.Parse(r.GetString(0)),
        EntityType = r.GetString(1),
        EntityId = Guid.Parse(r.GetString(2)),
        ExpectedRevision = r.GetInt64(3),
        Action = r.GetString(4),
        Status = r.GetString(5),
        LocalRowId = r.IsDBNull(6) ? null : r.GetInt64(6),
        SyncEpoch = r.IsDBNull(7) || string.IsNullOrEmpty(r.GetString(7)) ? null : Guid.Parse(r.GetString(7))
    };

    private static byte[] ValueBytes(SyncValue value) => value switch
    {
        HomeworkValue v => SyncJson.Serialize(v),
        CompletionValue v => SyncJson.Serialize(v),
        OverrideValue v => SyncJson.Serialize(v),
        FriendValue v => SyncJson.Serialize(v),
        SettingsValue v => SyncJson.Serialize(v),
        _ => throw new InvalidOperationException("Некорректный запрос синхронизации.")
    };

    private static bool EqualHomework(HomeworkValue a, HomeworkValue b)
        => a.Text == b.Text && a.SubjectKey == b.SubjectKey && a.TargetNthOccurrence == b.TargetNthOccurrence;

    private static bool EqualOverride(OverrideValue a, OverrideValue b)
        => a.DisplayName == b.DisplayName && a.Note == b.Note && a.Scope == b.Scope && a.SubjectKey == b.SubjectKey;

    private void Exec(string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
