using Vograph.Core.Models;
using Vograph.Core.Services.Sync;

namespace Vograph.Core.Services;

public class OverrideService
{
    private readonly Database _db;
    private readonly PrivateSyncOutbox? _outbox;
    public OverrideService(Database db, PrivateSyncOutbox? outbox = null)
    {
        _db = db;
        _outbox = outbox;
    }

    public string GetDisplayName(string subjectRaw, int dayOfWeek)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        var overrides = _db.GetOverrides();
        // global overrides weekday
        var global = overrides.FirstOrDefault(o => o.SubjectRawNormalized == norm && o.Scope == "global");
        if (global != null) return global.DisplayName;
        var weekday = overrides.FirstOrDefault(o => o.SubjectRawNormalized == norm && o.Scope == $"weekday:{dayOfWeek}");
        if (weekday != null) return weekday.DisplayName;
        return subjectRaw;
    }

    public Override? GetOverride(string subjectRaw, string scope)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        return _db.GetOverrides().FirstOrDefault(o => o.SubjectRawNormalized == norm && o.Scope == scope);
    }

    public long AddOrUpdate(string subjectRaw, string scope, string displayName, string? note)
    {
        if (_outbox is { Enabled: true })
            return _outbox.InTransaction(() => AddOrUpdateCore(subjectRaw, scope, displayName, note, enqueue: true));
        return AddOrUpdateCore(subjectRaw, scope, displayName, note, enqueue: false);
    }

    private long AddOrUpdateCore(string subjectRaw, string scope, string displayName, string? note, bool enqueue)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        var existing = GetOverride(subjectRaw, scope);
        if (enqueue && existing != null)
        {
            existing.EntityId ??= Guid.NewGuid();
            existing.CreatedAtUtc ??= DateTimeOffset.UtcNow;
            using var upd = _db.Connection.CreateCommand();
            upd.CommandText = "UPDATE overrides SET displayName=@d, note=@n, entityUuid=@u, createdAtUtc=@utc WHERE id=@id";
            upd.Parameters.AddWithValue("@d", displayName);
            upd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
            upd.Parameters.AddWithValue("@u", existing.EntityId.Value.ToString("D"));
            upd.Parameters.AddWithValue("@utc", existing.CreatedAtUtc.Value.ToString("o"));
            upd.Parameters.AddWithValue("@id", existing.Id);
            upd.ExecuteNonQuery();
            var value = PrivateSyncMapper.Override(norm, scope, displayName, note, existing.CreatedAtUtc.Value);
            _outbox!.Enqueue(Guid.NewGuid(), "override", existing.EntityId.Value, existing.Revision, "upsert", value, existing.Id);
            return existing.Id;
        }
        if (existing != null)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM overrides WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", existing.Id);
            cmd.ExecuteNonQuery();
        }
        var created = DateTime.UtcNow;
        var utc = new DateTimeOffset(created, TimeSpan.Zero);
        var ov = new Override
        {
            SubjectRawNormalized = norm,
            Scope = scope,
            DisplayName = displayName,
            Note = note,
            CreatedAt = created,
            EntityId = enqueue ? Guid.NewGuid() : null,
            CreatedAtUtc = enqueue ? utc : null
        };
        var id = _db.InsertOverride(ov);
        if (enqueue)
            _outbox!.Enqueue(Guid.NewGuid(), "override", ov.EntityId!.Value, 0, "upsert",
                PrivateSyncMapper.Override(norm, scope, displayName, note, utc), id);
        return id;
    }

    public void Remove(long id)
    {
        if (_outbox is { Enabled: true })
        {
            _outbox.InTransaction(() =>
            {
                var existing = _db.GetOverrides().FirstOrDefault(o => o.Id == id);
                if (existing is null) return 0;
                existing.EntityId ??= Guid.NewGuid();
                if (existing.Revision == 0)
                {
                    using var hard = _db.Connection.CreateCommand();
                    hard.CommandText = "DELETE FROM overrides WHERE id=@id";
                    hard.Parameters.AddWithValue("@id", id);
                    hard.ExecuteNonQuery();
                    _outbox.Enqueue(Guid.NewGuid(), "override", existing.EntityId.Value, 0, "delete", null, id);
                    return 0;
                }
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = "UPDATE overrides SET tombstone=1, entityUuid=@u WHERE id=@id";
                cmd.Parameters.AddWithValue("@u", existing.EntityId.Value.ToString("D"));
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                _outbox.Enqueue(Guid.NewGuid(), "override", existing.EntityId.Value, existing.Revision, "delete", null, id);
                return 0;
            });
            return;
        }
        using var legacy = _db.Connection.CreateCommand();
        legacy.CommandText = "DELETE FROM overrides WHERE id=@id";
        legacy.Parameters.AddWithValue("@id", id);
        legacy.ExecuteNonQuery();
    }

    public string? GetNote(string subjectRaw, int dayOfWeek)
    {
        var norm = ParityService.NormalizeSubject(subjectRaw);
        var overrides = _db.GetOverrides();
        var global = overrides.FirstOrDefault(o => o.SubjectRawNormalized == norm && o.Scope == "global");
        if (global != null) return global.Note;
        var weekday = overrides.FirstOrDefault(o => o.SubjectRawNormalized == norm && o.Scope == $"weekday:{dayOfWeek}");
        return weekday?.Note;
    }
}
