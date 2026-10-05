using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public sealed partial class PrivateSyncOutbox
{
    private static readonly string[] SyncPalette = ["#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C"];

    // Remote publication bypasses editing services: it must never echo into the local outbox.
    private void Project(SyncRecord record)
    {
        var id = record.EntityId.ToString("D");
        if (record.Tombstone && record.EntityType is not ("completion" or "settings"))
        {
            TombstoneLocal(record.EntityType, record.EntityId, record.Revision);
            return;
        }
        switch (record.EntityType)
        {
            case "homework" when record.Value is HomeworkValue v:
                var localDate = v.LegacyCreatedLocalDate ?? DateOnly.FromDateTime(v.CreatedAtUtc.ToOffset(TimeSpan.FromHours(3)).DateTime);
                Sql("""
                    INSERT INTO homework(subjectRawNormalized,text,createdAt,targetNthOccurrence,status,entityUuid,revision,tombstone,createdAtUtc,legacyCreatedLocalDate)
                    VALUES(@p0,@p1,@p2,@p3,'pending',@p4,@p5,0,@p6,@p7)
                    ON CONFLICT(entityUuid) WHERE entityUuid IS NOT NULL DO UPDATE SET
                        subjectRawNormalized=excluded.subjectRawNormalized,text=excluded.text,
                        targetNthOccurrence=excluded.targetNthOccurrence,dueDateComputed=NULL,
                        createdAt=excluded.createdAt,createdAtUtc=excluded.createdAtUtc,legacyCreatedLocalDate=excluded.legacyCreatedLocalDate,
                        revision=excluded.revision,tombstone=0
                    """, ParityService.NormalizeSubject(v.SubjectRaw), v.Text, localDate.ToString("yyyy-MM-dd"), v.TargetNthOccurrence, id, record.Revision,
                    v.CreatedAtUtc.ToString("o"), v.LegacyCreatedLocalDate?.ToString("yyyy-MM-dd"));
                ProjectCompletion(id);
                RememberAlias("homework", id);
                break;
            case "completion":
                var done = !record.Tombstone && record.Value is CompletionValue { Done: true };
                var completed = record.Value as CompletionValue;
                Sql("""
                    INSERT INTO homework_completion(entityUuid,done,doneAtUtc,revision,tombstone) VALUES(@p0,@p1,@p2,@p3,@p4)
                    ON CONFLICT(entityUuid) DO UPDATE SET done=excluded.done,doneAtUtc=excluded.doneAtUtc,
                        revision=excluded.revision,tombstone=excluded.tombstone
                    """, id, done ? 1 : 0, done ? completed?.DoneAtUtc?.ToString("o") : null, record.Revision, record.Tombstone ? 1 : 0);
                ProjectCompletion(id);
                break;
            case "override" when record.Value is OverrideValue v:
                Sql("""
                    INSERT INTO overrides(subjectRawNormalized,scope,displayName,note,createdAt,entityUuid,revision,tombstone,createdAtUtc)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,0,@p7)
                    ON CONFLICT(entityUuid) WHERE entityUuid IS NOT NULL DO UPDATE SET
                        subjectRawNormalized=excluded.subjectRawNormalized,scope=excluded.scope,displayName=excluded.displayName,
                        note=excluded.note,revision=excluded.revision,tombstone=0,createdAt=excluded.createdAt,createdAtUtc=excluded.createdAtUtc
                    """, ParityService.NormalizeSubject(v.SubjectRaw), v.Scope, v.DisplayName, v.Note, v.CreatedAtUtc.ToOffset(TimeSpan.FromHours(3)).DateTime.ToString("o"),
                    id, record.Revision, v.CreatedAtUtc.ToString("o"));
                RememberAlias("override", id);
                break;
            case "friend" when record.Value is FriendValue v:
                Sql("""
                    INSERT INTO friends(groupName,colorHex,enabled,memberNames,entityUuid,revision,tombstone)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,0)
                    ON CONFLICT(entityUuid) WHERE entityUuid IS NOT NULL DO UPDATE SET
                        groupName=excluded.groupName,colorHex=excluded.colorHex,enabled=excluded.enabled,
                        memberNames=excluded.memberNames,revision=excluded.revision,tombstone=0
                    """, v.GroupName, SyncPalette[v.PaletteIndex - 1], v.Enabled ? 1 : 0, v.MemberNames, id, record.Revision);
                RememberAlias("friend", id);
                break;
            case "settings":
                var s = record.Value as SettingsValue;
                Sql("""
                    UPDATE settings SET myGroupId=@p0,parityInvert=@p1,notifyTime1=@p2,notifyTime2=@p3,
                        intersectionStrictness=@p4,alwaysShowAllTrafficLights=@p5,entityUuid=@p6,revision=@p7,tombstone=@p8 WHERE id=1
                    """, s?.SelectedGroupId, s?.ParityInvert == true ? 1 : 0, s?.NotifyTime1, s?.NotifyTime2,
                    s?.Strictness ?? 25, s?.AlwaysShow == true ? 1 : 0, id, record.Revision, record.Tombstone ? 1 : 0);
                break;
            default: throw new InvalidOperationException("Неизвестная запись синхронизации.");
        }
    }

    private void ProjectCompletion(string id) => Sql("""
        UPDATE homework SET
            status=CASE WHEN EXISTS(SELECT 1 FROM homework_completion WHERE entityUuid=@p0 AND done=1 AND tombstone=0) THEN 'done' ELSE 'pending' END,
            doneAt=(SELECT doneAtUtc FROM homework_completion WHERE entityUuid=@p0 AND done=1 AND tombstone=0)
        WHERE entityUuid=@p0
        """, id);

    private void RememberAlias(string type, string id)
    {
        var table = type switch { "homework" => "homework", "friend" => "friends", "override" => "overrides", _ => throw new ArgumentException() };
        Sql($"INSERT OR REPLACE INTO sync_alias(entityType,localId,entityUuid) SELECT @p0,id,entityUuid FROM {table} WHERE entityUuid=@p1", type, id);
    }

    private void Sql(string sql, params object?[] values)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("p" + i, values[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
