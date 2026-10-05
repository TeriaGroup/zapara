using System.Text;
using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public sealed partial class PrivateSyncOutbox
{
    public bool ResolveConflict(SyncConflictDecision shown, Guid expectedEpoch, Action? checkCurrent = null) => InTransaction(() => {
        checkCurrent?.Invoke();
        if (SyncEpoch != expectedEpoch || shown.Kind == SyncConflictKind.Expired410 || shown.ServerRecord is not { } server) return false;
        var rows = Pending().Where(r => r.EntityType == shown.EntityType && r.EntityId == shown.EntityId).ToArray();
        if (rows.Length != 1 || rows[0].Status != "conflict") return false;
        var row = rows[0];
        var currentServer = Scalar("SELECT serverPayload FROM sync_draft WHERE entityType=@p0 AND entityId=@p1 AND opId=@p2",
            shown.EntityType, shown.EntityId.ToString("D"), row.OpId.ToString("D")) as string;
        if (currentServer is null || !Encoding.UTF8.GetBytes(currentServer).AsSpan().SequenceEqual(SyncJson.Serialize(server))) return false;
        var local = ReadPayload(row.OpId);
        if (shown.LocalValue is null ? local is not null : local is null || !local.AsSpan().SequenceEqual(ValueBytes(shown.LocalValue))) return false;
        if (RemoteRecord(shown.EntityType, shown.EntityId) is { } latest && latest.Revision > server.Revision) return false;
        if (shown.Kind == SyncConflictKind.KeepServer || (row.Action == "delete" && server.Tombstone))
        {
            DropConflict(row.EntityType, row.EntityId);
            Project(server);
            RememberRemote(server);
            if (row.EntityType == "completion" && RemoteRecord("homework", row.EntityId) is { Tombstone: true } parent)
                Project(parent);
            return true;
        }
        if (shown.NewOpId is not { } newOp || Find(newOp) is not null) return false;
        var parentGone = row.EntityType == "completion" && RemoteRecord("homework", row.EntityId) is { Tombstone: true };
        if ((row.EntityType == "homework" && server.Tombstone) || parentGone)
            return RecreateHomework(row, shown.LocalValue, newOp);
        if (row.EntityType == "settings" && server.Tombstone) return false;
        var target = server.Tombstone ? Guid.NewGuid() : row.EntityId;
        var revision = server.Tombstone ? 0 : server.Revision;
        var value = RebaseCreation(shown.LocalValue, server.Value);
        DropConflict(row.EntityType, row.EntityId);
        if (target != row.EntityId) MoveIdentity(row.EntityType, row.EntityId, target);
        Project(new(row.EntityType, target, Math.Max(1, revision), row.Action == "delete", server.ChangedAt, value));
        SetProjectionRevision(row.EntityType, target, revision);
        RememberRemote(server);
        Enqueue(newOp, row.EntityType, target, revision, row.Action, value, row.LocalRowId);
        return true;
    }, checkCurrent);

    private bool RecreateHomework(PrivateSyncOutboxEntry row, SyncValue? chosen, Guid newOp)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,subjectRawNormalized,text,targetNthOccurrence,createdAtUtc,legacyCreatedLocalDate,status,doneAt FROM homework WHERE entityUuid=@id";
        cmd.Parameters.AddWithValue("id", row.EntityId.ToString("D"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        var localId = r.GetInt64(0);
        var homework = chosen as HomeworkValue ?? new HomeworkValue(r.GetString(1), SyncValidation.NormalizeSubject(r.GetString(1)), r.GetString(2), r.GetInt32(3),
            DateTimeOffset.Parse(r.GetString(4)), r.IsDBNull(5) ? null : DateOnly.Parse(r.GetString(5)));
        var completion = chosen as CompletionValue ?? new CompletionValue(r.GetString(6) == "done", r.IsDBNull(7) ? null : DateTimeOffset.Parse(r.GetString(7)).ToUniversalTime());
        r.Close();
        var fresh = Guid.NewGuid();
        DropConflict("homework", row.EntityId);
        DropConflict("completion", row.EntityId);
        MoveIdentity("homework", row.EntityId, fresh);
        MoveIdentity("completion", row.EntityId, fresh);
        Project(new("homework", fresh, 1, false, DateTimeOffset.UtcNow, homework));
        Project(new("completion", fresh, 1, false, DateTimeOffset.UtcNow, completion));
        SetProjectionRevision("homework", fresh, 0);
        SetProjectionRevision("completion", fresh, 0);
        Enqueue(newOp, "homework", fresh, 0, "upsert", homework, localId);
        Enqueue(Guid.NewGuid(), "completion", fresh, 0, "upsert", completion, localId);
        return true;
    }

    private static SyncValue? RebaseCreation(SyncValue? local, SyncValue? server) => (local, server) switch {
        (HomeworkValue a, HomeworkValue b) => new HomeworkValue(a.SubjectRaw, a.SubjectKey, a.Text, a.TargetNthOccurrence, b.CreatedAtUtc, b.LegacyCreatedLocalDate),
        (OverrideValue a, OverrideValue b) => new OverrideValue(a.SubjectRaw, a.SubjectKey, a.Scope, a.DisplayName, a.Note, b.CreatedAtUtc),
        _ => local
    };
    private void DropConflict(string type, Guid id)
    {
        Sql("DELETE FROM sync_outbox WHERE entityType=@p0 AND entityId=@p1", type, id.ToString("D"));
        Sql("DELETE FROM sync_draft WHERE entityType=@p0 AND entityId=@p1", type, id.ToString("D"));
    }
    private void RememberRemote(SyncRecord record) => Sql("INSERT INTO sync_remote(entityType,entityId,record) VALUES(@p0,@p1,@p2) ON CONFLICT(entityType,entityId) DO UPDATE SET record=excluded.record",
        record.EntityType, record.EntityId.ToString("D"), SyncJson.Serialize(record));
    private static string ProjectionTable(string type) => type switch {
        "homework" => "homework", "completion" => "homework_completion", "friend" => "friends", "override" => "overrides", "settings" => "settings",
        _ => throw new ArgumentException("Неизвестная запись синхронизации.")
    };
    private void MoveIdentity(string type, Guid oldId, Guid newId) => Sql($"UPDATE {ProjectionTable(type)} SET entityUuid=@p0,revision=0 WHERE entityUuid=@p1", newId.ToString("D"), oldId.ToString("D"));
    private void SetProjectionRevision(string type, Guid id, long revision) => Sql($"UPDATE {ProjectionTable(type)} SET revision=@p0 WHERE entityUuid=@p1", revision, id.ToString("D"));
}
