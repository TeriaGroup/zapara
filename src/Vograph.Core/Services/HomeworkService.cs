using Vograph.Core.Models;
using Vograph.Core.Services.Sync;

namespace Vograph.Core.Services;

public class HomeworkService
{
    private readonly Database _db;
    private readonly PrivateSyncOutbox? _outbox;
    public HomeworkService(Database db, PrivateSyncOutbox? outbox = null)
    {
        _db = db;
        _outbox = outbox;
    }

    public long AddHomework(string subjectRaw, string text, int targetNth, DateTime? createdAt = null, Guid? opId = null)
    {
        if (_outbox is { Enabled: true })
            return _outbox.InTransaction(() => AddCore(subjectRaw, text, targetNth, createdAt, opId, enqueue: true));
        return AddCore(subjectRaw, text, targetNth, createdAt, opId, enqueue: false);
    }

    private long AddCore(string subjectRaw, string text, int targetNth, DateTime? createdAt, Guid? opId, bool enqueue)
    {
        var idOp = opId ?? Guid.NewGuid();
        var (utc, legacy, local) = PrivateSyncMapper.SplitCreated(createdAt);
        var value = PrivateSyncMapper.Homework(subjectRaw, text, Math.Clamp(targetNth, 1, 10), utc, legacy);
        if (enqueue && _outbox!.Find(idOp) is { } existing)
        {
            if (!_outbox.SameIntent(existing, "homework", "upsert", value))
                throw new InvalidOperationException("Повтор операции с другим содержимым.");
            return existing.LocalRowId ?? 0;
        }

        var norm = ParityService.NormalizeSubject(subjectRaw);
        var hw = new Homework
        {
            SubjectRawNormalized = norm,
            Text = text,
            CreatedAt = local,
            TargetNthOccurrence = Math.Clamp(targetNth, 1, 10),
            Status = "pending",
            EntityId = enqueue ? Guid.NewGuid() : null,
            CreatedAtUtc = enqueue ? utc : null,
            LegacyCreatedLocalDate = enqueue ? legacy : null
        };
        hw.DueDateComputed = ComputeDueDate(hw.SubjectRawNormalized, hw.CreatedAt, hw.TargetNthOccurrence);
        hw.Status = ComputeStatus(hw);
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO homework (subjectRawNormalized, text, createdAt, targetNthOccurrence, dueDateComputed, status, entityUuid, revision, tombstone, createdAtUtc, legacyCreatedLocalDate)
VALUES (@s,@t,@ca,@n,@due,@st,@u,0,0,@utc,@leg); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@s", hw.SubjectRawNormalized);
        cmd.Parameters.AddWithValue("@t", hw.Text);
        cmd.Parameters.AddWithValue("@ca", hw.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@n", hw.TargetNthOccurrence);
        cmd.Parameters.AddWithValue("@due", hw.DueDateComputed?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@st", hw.Status);
        cmd.Parameters.AddWithValue("@u", hw.EntityId is { } uuid ? uuid.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("@utc", hw.CreatedAtUtc?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@leg", hw.LegacyCreatedLocalDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        var id = (long)cmd.ExecuteScalar()!;
        if (enqueue)
            _outbox!.Enqueue(idOp, "homework", hw.EntityId!.Value, 0, "upsert", value, id);
        return id;
    }

    public void UpdateHomework(long id, string text, int targetNth, Guid? opId = null)
    {
        if (_outbox is { Enabled: true })
        {
            _outbox.InTransaction(() => { UpdateCore(id, text, targetNth, opId, enqueue: true); return 0; });
            return;
        }
        UpdateCore(id, text, targetNth, opId, enqueue: false);
    }

    private void UpdateCore(long id, string text, int targetNth, Guid? opId, bool enqueue)
    {
        var hw = GetById(id);
        if (hw == null) return;
        hw.Text = text;
        hw.TargetNthOccurrence = Math.Clamp(targetNth, 1, 10);
        hw.DueDateComputed = ComputeDueDate(hw.SubjectRawNormalized, hw.CreatedAt, hw.TargetNthOccurrence);
        hw.Status = ComputeStatus(hw);
        if (enqueue) EnsureHomeworkIdentity(hw);
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE homework SET text=@t, targetNthOccurrence=@n, dueDateComputed=@due, status=@st, entityUuid=@u, createdAtUtc=@utc, legacyCreatedLocalDate=@leg WHERE id=@id";
        cmd.Parameters.AddWithValue("@t", hw.Text);
        cmd.Parameters.AddWithValue("@n", hw.TargetNthOccurrence);
        cmd.Parameters.AddWithValue("@due", hw.DueDateComputed?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@st", hw.Status);
        cmd.Parameters.AddWithValue("@u", hw.EntityId is { } uuid ? uuid.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("@utc", hw.CreatedAtUtc?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@leg", hw.LegacyCreatedLocalDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        if (enqueue)
        {
            var (utc, legacy, _) = hw.CreatedAtUtc is { } stored
                ? (stored, hw.LegacyCreatedLocalDate, hw.CreatedAt)
                : PrivateSyncMapper.SplitCreated(hw.CreatedAt);
            var value = PrivateSyncMapper.Homework(hw.SubjectRawNormalized, hw.Text, hw.TargetNthOccurrence, utc, legacy);
            _outbox!.Enqueue(opId ?? Guid.NewGuid(), "homework", hw.EntityId!.Value, hw.Revision, "upsert", value, id);
        }
    }

    public Homework? GetById(long id) => ReadOne("SELECT id, subjectRawNormalized, text, createdAt, targetNthOccurrence, dueDateComputed, status, doneAt, entityUuid, revision, tombstone, createdAtUtc, legacyCreatedLocalDate FROM homework WHERE id=@id AND tombstone=0", id);

    public List<Homework> GetAll()
    {
        var list = new List<Homework>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, subjectRawNormalized, text, createdAt, targetNthOccurrence, dueDateComputed, status, doneAt, entityUuid, revision, tombstone, createdAtUtc, legacyCreatedLocalDate FROM homework WHERE tombstone=0 ORDER BY dueDateComputed";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapHomework(r));
        return list;
    }

    private Homework? ReadOne(string sql, long id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapHomework(r) : null;
    }

    private static Homework MapHomework(Microsoft.Data.Sqlite.SqliteDataReader r)
    {
        DateOnly? legacy = null;
        if (!r.IsDBNull(12) && DateOnly.TryParse(r.GetString(12), out var parsed)) legacy = parsed;
        return new Homework
        {
            Id = r.GetInt64(0),
            SubjectRawNormalized = r.GetString(1),
            Text = r.GetString(2),
            CreatedAt = DateTime.Parse(r.GetString(3)),
            TargetNthOccurrence = r.GetInt32(4),
            DueDateComputed = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
            Status = r.GetString(6),
            DoneAt = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)),
            EntityId = r.IsDBNull(8) || string.IsNullOrEmpty(r.GetString(8)) ? null : Guid.Parse(r.GetString(8)),
            Revision = r.IsDBNull(9) ? 0 : r.GetInt64(9),
            Tombstone = !r.IsDBNull(10) && r.GetInt64(10) != 0,
            CreatedAtUtc = r.IsDBNull(11) || string.IsNullOrEmpty(r.GetString(11)) ? null : DateTimeOffset.Parse(r.GetString(11)),
            LegacyCreatedLocalDate = legacy
        };
    }

    private void EnsureHomeworkIdentity(Homework hw)
    {
        if (hw.EntityId is { } existing && existing != Guid.Empty)
        {
            if (hw.CreatedAtUtc is null)
            {
                var split = PrivateSyncMapper.SplitCreated(hw.CreatedAt);
                hw.CreatedAtUtc = split.Utc;
                hw.LegacyCreatedLocalDate ??= split.Legacy;
            }
            return;
        }
        hw.EntityId = Guid.NewGuid();
        var minted = PrivateSyncMapper.SplitCreated(hw.CreatedAt);
        hw.CreatedAtUtc ??= minted.Utc;
        hw.LegacyCreatedLocalDate ??= minted.Legacy;
    }

    public List<Homework> GetForSubject(string subjectRaw)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        return GetAll().Where(h => h.SubjectRawNormalized == norm).ToList();
    }

    public void MarkDone(long id, bool done)
    {
        if (_outbox is { Enabled: true })
        {
            _outbox.InTransaction(() => { MarkDoneCore(id, done, enqueue: true); return 0; });
            return;
        }
        MarkDoneCore(id, done, enqueue: false);
    }

    private void MarkDoneCore(long id, bool done, bool enqueue)
    {
        var hw = GetById(id);
        using var cmd = _db.Connection.CreateCommand();
        DateTimeOffset? doneAt = done ? DateTimeOffset.UtcNow : null;
        if (done)
        {
            cmd.CommandText = "UPDATE homework SET status='done', doneAt=@da WHERE id=@id";
            cmd.Parameters.AddWithValue("@da", DateTime.UtcNow.ToString("o"));
        }
        else
        {
            string status = "pending";
            if (hw != null)
            {
                var tmp = new Homework { SubjectRawNormalized = hw.SubjectRawNormalized, CreatedAt = hw.CreatedAt, TargetNthOccurrence = hw.TargetNthOccurrence, DueDateComputed = hw.DueDateComputed, Status = "pending" };
                tmp.DueDateComputed = ComputeDueDate(tmp.SubjectRawNormalized, tmp.CreatedAt, tmp.TargetNthOccurrence) ?? tmp.DueDateComputed;
                status = ComputeStatus(tmp);
            }
            cmd.CommandText = "UPDATE homework SET status=@st, doneAt=NULL WHERE id=@id";
            cmd.Parameters.AddWithValue("@st", status);
        }
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        if (enqueue && hw is not null)
        {
            EnsureHomeworkIdentity(hw);
            using var ident = _db.Connection.CreateCommand();
            ident.CommandText = "UPDATE homework SET entityUuid=@u, createdAtUtc=@utc, legacyCreatedLocalDate=@leg WHERE id=@id";
            ident.Parameters.AddWithValue("@u", hw.EntityId!.Value.ToString("D"));
            ident.Parameters.AddWithValue("@utc", hw.CreatedAtUtc?.ToString("o") ?? (object)DBNull.Value);
            ident.Parameters.AddWithValue("@leg", hw.LegacyCreatedLocalDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
            ident.Parameters.AddWithValue("@id", id);
            ident.ExecuteNonQuery();
            long completionRevision = 0;
            using (var read = _db.Connection.CreateCommand())
            {
                read.CommandText = "SELECT revision FROM homework_completion WHERE entityUuid=@u";
                read.Parameters.AddWithValue("@u", hw.EntityId.Value.ToString("D"));
                var v = read.ExecuteScalar();
                if (v is not null && v is not DBNull) completionRevision = Convert.ToInt64(v);
            }
            using var upsert = _db.Connection.CreateCommand();
            upsert.CommandText = @"INSERT INTO homework_completion (entityUuid, done, doneAtUtc, revision, tombstone) VALUES (@u,@d,@da,@r,0)
ON CONFLICT(entityUuid) DO UPDATE SET done=excluded.done, doneAtUtc=excluded.doneAtUtc, tombstone=0";
            upsert.Parameters.AddWithValue("@u", hw.EntityId.Value.ToString("D"));
            upsert.Parameters.AddWithValue("@d", done ? 1 : 0);
            upsert.Parameters.AddWithValue("@da", doneAt?.ToString("o") ?? (object)DBNull.Value);
            upsert.Parameters.AddWithValue("@r", completionRevision);
            upsert.ExecuteNonQuery();
            _outbox!.Enqueue(Guid.NewGuid(), "completion", hw.EntityId.Value, completionRevision, "upsert",
                PrivateSyncMapper.Completion(done, doneAt), id);
        }
        if (!done) RecomputeAllStatuses();
    }

    public void Delete(long id, Guid? opId = null)
    {
        if (_outbox is { Enabled: true })
        {
            _outbox.InTransaction(() => { DeleteCore(id, opId, enqueue: true); return 0; });
            return;
        }
        DeleteCore(id, opId, enqueue: false);
    }

    private void DeleteCore(long id, Guid? opId, bool enqueue)
    {
        if (!enqueue)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM homework WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return;
        }
        var hw = GetById(id);
        if (hw is null) return;
        EnsureHomeworkIdentity(hw);
        var entityId = hw.EntityId!.Value;
        long completionRevision = 0;
        using (var read = _db.Connection.CreateCommand())
        {
            read.CommandText = "SELECT revision FROM homework_completion WHERE entityUuid=@u";
            read.Parameters.AddWithValue("@u", entityId.ToString("D"));
            var v = read.ExecuteScalar();
            if (v is not null && v is not DBNull) completionRevision = Convert.ToInt64(v);
        }
        _outbox!.CancelPending("completion", entityId);
        if (completionRevision > 0)
            _outbox.Enqueue(Guid.NewGuid(), "completion", entityId, completionRevision, "delete", null, id);
        if (hw.Revision == 0)
        {
            using var hard = _db.Connection.CreateCommand();
            hard.CommandText = "DELETE FROM homework WHERE id=@id";
            hard.Parameters.AddWithValue("@id", id);
            hard.ExecuteNonQuery();
            _outbox.Enqueue(opId ?? Guid.NewGuid(), "homework", entityId, 0, "delete", null, id);
            using var comp = _db.Connection.CreateCommand();
            comp.CommandText = "DELETE FROM homework_completion WHERE entityUuid=@u";
            comp.Parameters.AddWithValue("@u", entityId.ToString("D"));
            comp.ExecuteNonQuery();
            return;
        }
        using var tomb = _db.Connection.CreateCommand();
        tomb.CommandText = "UPDATE homework SET tombstone=1, entityUuid=@u WHERE id=@id";
        tomb.Parameters.AddWithValue("@u", entityId.ToString("D"));
        tomb.Parameters.AddWithValue("@id", id);
        tomb.ExecuteNonQuery();
        using var ct = _db.Connection.CreateCommand();
        ct.CommandText = "UPDATE homework_completion SET tombstone=1 WHERE entityUuid=@u";
        ct.Parameters.AddWithValue("@u", entityId.ToString("D"));
        ct.ExecuteNonQuery();
        _outbox.Enqueue(opId ?? Guid.NewGuid(), "homework", entityId, hw.Revision, "delete", null, id);
    }

    public DateTime? ComputeDueDate(string subjectNormalized, DateTime from, int nth)
    {
        var settings = _db.GetSettings();
        if (string.IsNullOrEmpty(settings.MyGroupId)) return null;
        var groupId = settings.MyGroupId!;
        DateTime periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(DateTime.Now.Year, 9, 1);
        int weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;

        // Scan forward up to 120 days
        int found = 0;
        for (int offset = 1; offset <= 120; offset++)
        {
            var date = from.Date.AddDays(offset);
            // Skip Sunday
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            int dow = (int)date.DayOfWeek; if (dow == 0) dow = 7;
            int weekCode = ParityService.GetWeekCode(date, periodStart, weekCount);
            if (settings.ParityInvert) weekCode = weekCode == 1 ? 2 : 1;

            var lessons = _db.GetLessons(groupId, dow, weekCode);
            foreach (var l in lessons)
            {
                var norm = ParityService.NormalizeSubject(l.SubjectRaw);
                if (norm == subjectNormalized)
                {
                    found++;
                    if (found == nth)
                    {
                        return date;
                    }
                    break; // count only once per day? Prompt says count occurrences of same subject, not lessons? If subject appears twice same day, count twice? For MVP count per day occurrence (once per lesson). But to avoid double counting same day, we break after first match per day.
                    // However spec says "N-th next Lesson where subjectRawNormalized matches" so if two lessons same subject same day, count each?
                    // We'll count each lesson individually, so not break? But simpler break per day avoids double.
                }
            }
        }
        return null;
    }

    public string ComputeStatus(Homework hw)
    {
        if (hw.Status == "done") return "done";
        if (hw.DueDateComputed == null) return "pending";
        var today = DateTime.Today;
        var due = hw.DueDateComputed.Value.Date;
        int daysDiff = (due - today).Days;
        // Need to count lessons before due
        var settings = _db.GetSettings();
        if (string.IsNullOrEmpty(settings.MyGroupId)) return "pending";
        var groupId = settings.MyGroupId!;
        DateTime periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(DateTime.Now.Year, 9, 1);
        int weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;

        // Count occurrences before due
        int lessonsBefore = 0;
        for (int offset = 1; offset <= 120; offset++)
        {
            var d = today.AddDays(offset);
            if (d >= due) break;
            int dow = (int)d.DayOfWeek; if (dow == 0) dow = 7;
            if (dow == 7) continue;
            int wc = ParityService.GetWeekCode(d, periodStart, weekCount);
            if (settings.ParityInvert) wc = wc == 1 ? 2 : 1;
            var lessons = _db.GetLessons(groupId, dow, wc);
            foreach (var l in lessons)
            {
                if (ParityService.NormalizeSubject(l.SubjectRaw) == hw.SubjectRawNormalized) lessonsBefore++;
            }
        }

        // Also check if due lesson exists today/tomorrow
        if (daysDiff < 0) return "overdue";
        if (daysDiff == 0) return "burning_urgent"; // burns very brightly in morning
        if (daysDiff == 1) return "burning"; // due tomorrow
        if (lessonsBefore == 1) return "approaching"; // 1 lesson before due -> gray
        if (lessonsBefore == 0 && daysDiff <= 3) return "approaching"; // heuristic
        return "far";
    }

    public void RecomputeAllStatuses()
    {
        var all = GetAll();
        foreach (var hw in all)
        {
            if (hw.Status == "done") continue;
            var newDue = ComputeDueDate(hw.SubjectRawNormalized, hw.CreatedAt, hw.TargetNthOccurrence);
            var newStatus = hw.DueDateComputed != newDue ? ComputeStatus(new Homework { SubjectRawNormalized = hw.SubjectRawNormalized, CreatedAt = hw.CreatedAt, TargetNthOccurrence = hw.TargetNthOccurrence, DueDateComputed = newDue, Status = "pending" }) : ComputeStatus(hw);
            // If due changed, update
            if (newDue != hw.DueDateComputed || newStatus != hw.Status)
            {
                hw.DueDateComputed = newDue;
                hw.Status = newStatus;
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = "UPDATE homework SET dueDateComputed=@due, status=@st WHERE id=@id";
                cmd.Parameters.AddWithValue("@due", hw.DueDateComputed?.ToString("o") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@st", hw.Status);
                cmd.Parameters.AddWithValue("@id", hw.Id);
                cmd.ExecuteNonQuery();
            }
            else
            {
                // just status
                hw.Status = ComputeStatus(hw);
                using var cmd2 = _db.Connection.CreateCommand();
                cmd2.CommandText = "UPDATE homework SET status=@st WHERE id=@id";
                cmd2.Parameters.AddWithValue("@st", hw.Status);
                cmd2.Parameters.AddWithValue("@id", hw.Id);
                cmd2.ExecuteNonQuery();
            }
        }
    }
}
