using Microsoft.Data.Sqlite;
using System.Collections.Immutable;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class TimetableApiCacheMigrationTests
{
    [Fact]
    public void Old_schema_reopens_without_wipe_and_preserves_personal_ids()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vograph-api-migration-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new Database(path))
            {
                db.UpsertGroup(new Group { Id = "old", Name = "Old", RawXml = "original" });
                db.InsertLesson(new Lesson { GroupId = "old", SubjectRaw = "personal" });
                db.InsertFriend(new FriendGroup { GroupName = "Old", Enabled = true });
                db.InsertOverride(new Override { SubjectRawNormalized = "personal", DisplayName = "Mine", Scope = "global" });
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "DROP TABLE api_catalog; DROP TABLE api_cache_metadata; ALTER TABLE settings DROP COLUMN lastAutoCheckAt";
                cmd.ExecuteNonQuery();
            }
            using (var db = new Database(path))
            {
                Assert.Single(db.GetAllLessonsForGroup("old"));
                Assert.Equal(1, Assert.Single(db.GetFriends()).Id);
                Assert.Equal(1, Assert.Single(db.GetOverrides()).Id);
                Assert.Equal("Mine", Assert.Single(db.GetOverrides()).DisplayName);
                Assert.Null(new TimetableApiCache(db).Read("old"));
                new TimetableApiCache(db).Apply(TimetableApiCacheTests.Snapshot());
                Assert.Single(db.GetAllLessonsForGroup("old"));
            }
        }
        finally
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            SqliteConnection.ClearPool(connection);
            File.Delete(path);
        }
    }

    [Fact]
    public void Unfetched_groups_keep_own_period_and_missing_catalog_does_not_delete_cache()
    {
        using var db = new Database(":memory:") { UseApiCatalog = true };
        var cache = new TimetableApiCache(db);
        var first = TimetableApiCacheTests.Snapshot("1", "2");
        cache.Apply(first);
        var newer = TimetableApiCacheTests.Snapshot("1");
        newer = newer with { Period = newer.Period with { Start = new(2027, 2, 1), Title = "Spring" },
            Groups = newer.Groups.Where(g => g.Id != "2").ToImmutableArray() };
        cache.Apply(newer);
        Assert.DoesNotContain(db.GetAllGroups(), g => g.Id == "2");
        Assert.NotNull(db.GetGroup("2"));
        Assert.NotNull(db.GetGroupByName("B"));
        Assert.Single(db.GetAllLessonsForGroup("2"));
        Assert.Equal(first.Meta.SnapshotId, cache.Read("2")!.Meta!.SnapshotId);
        var settings = db.GetSettings(); settings.MyGroupId = "2"; db.SaveSettings(settings);
        Assert.Equal("2026-09-01", db.GetSettings().PeriodStart);
        Assert.False(cache.CanIntersect("1", "2"));
        settings.MyGroupId = "1"; db.SaveSettings(settings);
        Assert.Equal("2027-02-01", db.GetSettings().PeriodStart);
    }
}
