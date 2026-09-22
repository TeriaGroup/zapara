using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public sealed partial class PrivateSyncOutbox
{
    public bool SnapshotReady => SyncEpoch is { } epoch && Scalar("SELECT readyEpoch FROM sync_inbox WHERE id=1") as string == epoch.ToString("D");
    public SyncResyncManifest? SnapshotManifest => Scalar("SELECT manifest FROM sync_inbox WHERE id=1") is byte[] bytes
        ? SyncJson.Parse<SyncResyncManifest>(bytes) : null;
    public long SnapshotAfterOrdinal => Convert.ToInt64(Scalar("SELECT afterOrdinal FROM sync_inbox WHERE id=1"));

    public void RequireSnapshot() => Sql("UPDATE sync_inbox SET readyEpoch=NULL WHERE id=1");
    public void DiscardSnapshot() => InTransaction(() => { ClearStage(); return 0; });
    public void BeginSnapshot(SyncResyncManifest manifest) => InTransaction(() => {
        ClearStage();
        Sql("UPDATE sync_inbox SET manifest=@p0 WHERE id=1", SyncJson.Serialize(manifest));
        return 0;
    });

    public void StageSnapshot(SyncResyncPage page, Action? checkCurrent = null) => InTransaction(() => {
        checkCurrent?.Invoke();
        if (SnapshotManifest != page.Manifest || SnapshotAfterOrdinal != page.AfterOrdinal)
            throw new InvalidOperationException("Снимок синхронизации изменился.");
        foreach (var item in page.Items)
            Sql("INSERT INTO sync_stage(entityType,entityId,ordinal,record) VALUES(@p0,@p1,@p2,@p3)",
                item.Record.EntityType, item.Record.EntityId.ToString("D"), item.Ordinal, SyncJson.Serialize(item.Record));
        Sql("UPDATE sync_inbox SET afterOrdinal=@p0 WHERE id=1", page.NextAfterOrdinal);
        return 0;
    }, checkCurrent);

    public void PublishSnapshot(Action? checkCurrent = null) => InTransaction(() => {
        checkCurrent?.Invoke();
        var manifest = SnapshotManifest ?? throw new InvalidOperationException("Нет снимка синхронизации.");
        var staged = ReadRecords("SELECT record FROM sync_stage ORDER BY ordinal");
        if (SnapshotAfterOrdinal != manifest.ItemCount || staged.Count != manifest.ItemCount)
            throw new InvalidOperationException("Снимок синхронизации не завершён.");
        var keys = staged.Select(r => (r.EntityType, r.EntityId)).ToHashSet();
        var reset = SyncEpoch is { } previous && previous != manifest.SyncEpoch;
        foreach (var (type, id) in KnownSyncedIdentities())
            if (!keys.Contains((type, id)))
                Receive(new(type, id, Math.Max(1, manifest.HighWater), true, manifest.CreatedAt, null), force: true, reset: true);
        foreach (var record in staged.OrderBy(r => r.EntityType == "completion" ? 1 : 0))
            Receive(record, force: true, reset: reset);
        SetEpoch(manifest.SyncEpoch, manifest.HighWater);
        Sql("UPDATE sync_inbox SET readyEpoch=@p0 WHERE id=1", manifest.SyncEpoch.ToString("D"));
        ClearStage();
        return 0;
    }, checkCurrent);

    public bool ApplyChanges(SyncChangesPage page, Action? checkCurrent = null) => InTransaction(() => {
        checkCurrent?.Invoke();
        if (!SnapshotReady || SyncEpoch != page.Metadata.SyncEpoch || AfterSequence != page.AfterSequence)
            throw new InvalidOperationException("Курсор синхронизации изменился.");
        foreach (var change in page.Changes)
        {
            var row = Find(change.OpId);
            if (row is not null && row.SyncEpoch == page.Metadata.SyncEpoch && row.EntityType == change.Record.EntityType &&
                row.EntityId == change.Record.EntityId && row.ExpectedRevision < change.Record.Revision &&
                ExactIntent(row, change.Record)) ApplyAck(row, change.Record);
            Receive(change.Record);
        }
        SetEpoch(page.Metadata.SyncEpoch, page.NextAfterSequence);
        return page.Changes.Count > 0;
    }, checkCurrent);

    private bool ExactIntent(PrivateSyncOutboxEntry row, SyncRecord record)
    {
        if ((row.Action == "delete") != record.Tombstone) return false;
        var payload = ReadPayload(row.OpId);
        return record.Tombstone ? payload is null : payload is not null && record.Value is not null &&
            payload.AsSpan().SequenceEqual(ValueBytes(record.Value));
    }

    public SyncRecord? RemoteRecord(string type, Guid id) => Scalar("SELECT record FROM sync_remote WHERE entityType=@p0 AND entityId=@p1", type, id.ToString("D")) is byte[] bytes
        ? SyncJson.Parse<SyncRecord>(bytes) : null;

    private void Receive(SyncRecord record, bool force = false, bool reset = false)
    {
        var previous = RemoteRecord(record.EntityType, record.EntityId);
        if (!force && previous is not null && previous.Revision >= record.Revision) return;
        if (!reset && previous is { Tombstone: false } && !record.Tombstone && CreationChanged(previous.Value, record.Value))
            throw new InvalidOperationException("Изменены неизменяемые данные синхронизации.");
        Sql("INSERT INTO sync_remote(entityType,entityId,record) VALUES(@p0,@p1,@p2) ON CONFLICT(entityType,entityId) DO UPDATE SET record=excluded.record",
            record.EntityType, record.EntityId.ToString("D"), SyncJson.Serialize(record));
        var pending = Pending().Where(p => p.EntityId == record.EntityId && (p.EntityType == record.EntityType ||
            (record.EntityType == "homework" && record.Tombstone && p.EntityType == "completion"))).ToArray();
        if (pending.Length > 0)
        {
            // The server may have committed a write whose response was lost. Retain its
            // original stamped operation so retry can recover the idempotent receipt.
            if (!reset && pending.Any(p => p.SyncEpoch is not null && p.ExpectedRevision < record.Revision && ExactIntent(p, record))) return;
            foreach (var row in pending)
                if (reset || record.Revision > row.ExpectedRevision)
                {
                    var conflictRecord = row.EntityType == record.EntityType ? record :
                        new SyncRecord(row.EntityType, record.EntityId, record.Revision, true, record.ChangedAt, null);
                    MarkConflict(row, new(409, "revision_conflict", new(SyncEpoch ?? SnapshotManifest!.SyncEpoch, record.Revision, 0), conflictRecord));
                }
            return;
        }
        if (Drafts().Any(d => d.EntityType == record.EntityType && d.EntityId == record.EntityId)) return;
        Project(record);
    }

    private static bool CreationChanged(SyncValue? before, SyncValue? after) => (before, after) switch {
        (HomeworkValue a, HomeworkValue b) => a.CreatedAtUtc != b.CreatedAtUtc || a.LegacyCreatedLocalDate != b.LegacyCreatedLocalDate,
        (OverrideValue a, OverrideValue b) => a.CreatedAtUtc != b.CreatedAtUtc,
        _ => false
    };

    private List<(string, Guid)> KnownSyncedIdentities()
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 'homework',entityUuid FROM homework WHERE revision>0 AND entityUuid IS NOT NULL
            UNION SELECT 'completion',entityUuid FROM homework_completion WHERE revision>0
            UNION SELECT 'friend',entityUuid FROM friends WHERE revision>0 AND entityUuid IS NOT NULL
            UNION SELECT 'override',entityUuid FROM overrides WHERE revision>0 AND entityUuid IS NOT NULL
            UNION SELECT 'settings',entityUuid FROM settings WHERE revision>0 AND entityUuid IS NOT NULL
            """;
        using var reader = cmd.ExecuteReader();
        var rows = new List<(string, Guid)>();
        while (reader.Read()) rows.Add((reader.GetString(0), Guid.Parse(reader.GetString(1))));
        return rows;
    }

    private List<SyncRecord> ReadRecords(string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<SyncRecord>();
        while (reader.Read()) rows.Add(SyncJson.Parse<SyncRecord>((byte[])reader.GetValue(0)));
        return rows;
    }
    private void ClearStage() { Sql("DELETE FROM sync_stage"); Sql("UPDATE sync_inbox SET manifest=NULL,afterOrdinal=0 WHERE id=1"); }
    private object? Scalar(string sql, params object?[] values)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("p" + i, values[i] ?? DBNull.Value);
        return cmd.ExecuteScalar();
    }
}
