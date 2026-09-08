using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class TimetableApiCacheTests
{
    internal static TimetableApiSnapshot Snapshot(params string[] downloaded)
    {
        var meta = new TimetableApiMeta(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, "file", null, new string('a', 64), false);
        var refresh = new TimetableApiRefresh(null, null, null, null, null, false);
        var groups = ImmutableArray.Create(new TimetableApiGroup("1", "A", 1),
            new TimetableApiGroup("2", "B", 1), new TimetableApiGroup("9999", "Empty", 0));
        var lesson = new TimetableApiLesson(1, 1, 1, "09:00", "10:35", "Math", "math", null, null, null, null, null);
        return new(new(new DateOnly(2026, 9, 1), 2, "Autumn", "Europe/Moscow"), meta, refresh, groups,
            groups.Where(g => downloaded.Contains(g.Id)).ToImmutableDictionary(g => g.Id,
                g => new TimetableApiDownloadedGroup(g, meta, refresh, g.LessonCount == 0 ? [] : [lesson])));
    }

    internal static string Dump(Database db)
    {
        var values = new List<string>();
        foreach (var table in new[] { "groups", "schedule_cache", "settings", "api_catalog", "api_cache_metadata" })
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {table} ORDER BY 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) values.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        }
        return string.Join("\n", values);
    }

    [Fact]
    public void Catalog_only_is_not_downloaded_empty_and_does_not_replace_existing_raw_cache()
    {
        using var db = new Database(":memory:");
        db.UpsertGroup(new Group { Id = "1", Name = "Old", RawXml = "original" });
        db.InsertLesson(new Lesson { GroupId = "1", SubjectRaw = "old" });
        new TimetableApiCache(db).Apply(Snapshot());
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM api_cache_metadata WHERE groupId='9999'";
        Assert.Equal(0L, cmd.ExecuteScalar());
        cmd.CommandText = "SELECT rawXml FROM groups WHERE id='1'";
        Assert.Equal("original", cmd.ExecuteScalar());
        Assert.Null(db.GetSettings().LastFetchedAt);
        new TimetableApiCache(db).Apply(Snapshot("9999"));
        cmd.CommandText = "SELECT COUNT(*) FROM api_cache_metadata WHERE groupId='9999'";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    [Fact]
    public void Trigger_failure_rolls_back_multiple_groups_catalog_and_metadata()
    {
        using var db = new Database(":memory:");
        var cache = new TimetableApiCache(db);
        cache.Apply(Snapshot("1", "2"));
        var before = Dump(db);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "CREATE TRIGGER fail_second BEFORE INSERT ON schedule_cache WHEN NEW.groupId='2' BEGIN SELECT RAISE(ABORT,'test'); END";
        cmd.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => cache.Apply(Snapshot("1", "2")));
        Assert.Equal(before, Dump(db));
    }

    [Fact]
    public void Newly_listed_group_never_inherits_selected_group_fetched_state()
    {
        using var db = new Database(":memory:") { UseApiCatalog = true };
        var cache = new TimetableApiCache(db);
        var first = Snapshot("1");
        cache.Apply(first);
        var settings = db.GetSettings(); settings.MyGroupId = "1"; db.SaveSettings(settings);
        var withNew = Snapshot("1");
        withNew = withNew with { Groups = withNew.Groups.Add(new("new", "New", 0)) };
        cache.Apply(withNew);
        cache.Apply(withNew);
        Assert.Null(cache.Read("new"));
        Assert.Null(db.GetGroup("new")!.LastFetchedAt);
    }

    [Fact]
    public void Constructed_zero_snapshot_is_rejected_before_writing()
    {
        using var db = new Database(":memory:");
        var snapshot = Snapshot();
        Assert.Throws<TimetableApiException>(() => new TimetableApiCache(db).Apply(snapshot with { Meta = snapshot.Meta with { SnapshotId = Guid.Empty } }));
        Assert.Empty(db.GetAllGroups());
    }
}
