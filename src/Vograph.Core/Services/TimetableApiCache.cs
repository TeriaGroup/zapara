using Vograph.Core.Models;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Vograph.Core.Services;

public sealed class TimetableApiCache(Database db)
{
    public static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS api_catalog (groupId TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS api_cache_metadata (groupId TEXT PRIMARY KEY, snapshotId TEXT, payload TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    public CacheMetadata? Read(string? groupId)
    {
        if (groupId is null) return null;
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT payload FROM api_cache_metadata WHERE groupId=@id";
        cmd.Parameters.AddWithValue("@id", groupId);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<CacheMetadata>(json) : null;
    }

    public bool CanIntersect(string selected, string friend)
    {
        var mine = Read(selected);
        var other = Read(friend);
        if (mine?.Source != "api" && other?.Source != "api") return true;
        return mine?.Source == "api" && other?.Source == "api" && mine.SourceBase == other.SourceBase &&
            mine.Period == other.Period && mine.Meta?.SnapshotId == other.Meta?.SnapshotId &&
            mine.Meta?.Stale == false && other.Meta?.Stale == false;
    }

    public void Apply(TimetableApiSnapshot snapshot, string sourceBase = "")
    {
        Validate(snapshot);
        using var tx = db.Connection.BeginTransaction();
        // Capture old legacy periods before any later settings save can copy the selected API period.
        var settings = db.GetSettings();
        var firstAdoption = Read("") is null;
        var oldPeriod = new TimetableApiPeriod(DateOnly.TryParse(settings.PeriodStart, out var start) ? start : new(DateTime.Now.Year, 9, 1),
            settings.WeekCount, settings.PeriodTitle ?? "", "Europe/Moscow");
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, lastFetchedAt FROM groups";
            var old = new List<(string Id, string? Fetched)>();
            using (var reader = cmd.ExecuteReader())
                while (reader.Read()) old.Add((reader.GetString(0), reader.IsDBNull(1) ? (firstAdoption ? settings.LastFetchedAt : null) : reader.GetString(1)));
            foreach (var group in old)
                if (Read(group.Id) is null && (group.Fetched is not null || db.GetAllLessonsForGroup(group.Id).Count > 0))
                    Write(group.Id, new(oldPeriod, null, group.Fetched, "legacy", ""), tx);
        }
        Execute("DELETE FROM api_catalog", tx);
        foreach (var group in snapshot.Groups)
        {
            Execute("INSERT INTO groups(id,name) VALUES(@id,@name) ON CONFLICT(id) DO UPDATE SET name=excluded.name", tx,
                ("@id", group.Id), ("@name", group.Name));
            Execute("INSERT INTO api_catalog(groupId,name) VALUES(@id,@name)", tx, ("@id", group.Id), ("@name", group.Name));
        }
        Write("", new(snapshot.Period, snapshot.Meta, snapshot.Meta.FetchedAt.ToString("o"), "api", sourceBase), tx);
        foreach (var (id, downloaded) in snapshot.DownloadedGroups.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var group = downloaded.Group.ToGroup();
            Execute("UPDATE groups SET rawXml=NULL,url='',lastFetchedAt=@at WHERE id=@id", tx,
                ("@at", downloaded.Meta.FetchedAt.ToString("o")), ("@id", group.Id));
            db.ClearScheduleForGroup(id);
            foreach (var lesson in downloaded.Lessons) db.InsertLesson(lesson.ToLesson(id));
            Write(id, new(snapshot.Period, downloaded.Meta, downloaded.Meta.FetchedAt.ToString("o"), "api", sourceBase), tx);
        }
        Execute("UPDATE settings SET lastAutoCheckAt=@at WHERE id=1", tx, ("@at", DateTime.UtcNow.ToString("o")));
        tx.Commit();
        // Derived homework is deliberately outside this transaction; caller logs recompute failures safely.
    }

    private void Write(string id, CacheMetadata metadata, SqliteTransaction tx) => Execute(
        "INSERT INTO api_cache_metadata(groupId,snapshotId,payload) VALUES(@id,@snapshot,@payload) ON CONFLICT(groupId) DO UPDATE SET snapshotId=excluded.snapshotId,payload=excluded.payload",
        tx, ("@id", id), ("@snapshot", metadata.Meta?.SnapshotId.ToString()), ("@payload", JsonSerializer.Serialize(metadata)));

    private void Execute(string sql, SqliteTransaction tx, params (string Name, string? Value)[] parameters)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void Validate(TimetableApiSnapshot snapshot)
    {
        // Trusted transport boundary, not a second JSON validator. Public records must not poison keys/generation.
        if (snapshot.Meta.SnapshotId == Guid.Empty || snapshot.Groups.IsDefault || snapshot.Period.WeekCount != 2 ||
            snapshot.Period.TimeZone != "Europe/Moscow" || snapshot.Groups.Any(g => string.IsNullOrWhiteSpace(g.Id) || g.LessonCount < 0) ||
            snapshot.Groups.Select(g => g.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Groups.Length)
            throw new TimetableApiException(TimetableApiFailure.InvalidPayload);
        foreach (var (id, group) in snapshot.DownloadedGroups)
            if (group.Group.Id != id || !snapshot.Groups.Contains(group.Group) || group.Meta.SnapshotId != snapshot.Meta.SnapshotId ||
                group.Lessons.IsDefault || group.Lessons.Length != group.Group.LessonCount)
                throw new TimetableApiException(TimetableApiFailure.InvalidPayload);
    }
}

public sealed record CacheMetadata(TimetableApiPeriod Period, TimetableApiMeta? Meta, string? FetchedAt, string Source, string SourceBase);
