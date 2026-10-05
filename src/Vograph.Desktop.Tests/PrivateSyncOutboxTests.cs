using Microsoft.Data.Sqlite;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncOutboxTests
{
    private static readonly DateTime Created = new(2026, 9, 5, 12, 0, 0);
    private static readonly Uri ScopeUri = new("http://127.0.0.1/outbox-test/");

    [Fact]
    public void Guest_homework_mutations_leave_outbox_empty()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        db.Services.Homework.UpdateHomework(db.Services.Homework.GetAll()[0].Id, "глава 2", 1);
        Assert.False(db.Services.Outbox.Enabled);
        Assert.Equal(0, CountOutbox(db.Services.Db));
        Assert.Empty(db.Services.Outbox.Pending());
    }

    [Fact]
    public void Account_add_commits_homework_and_outbox_together()
    {
        using var dir = new ProfileTestDirectory();
        long id;
        Guid opId;
        using (var app = OpenAccount(dir.Root))
        {
            Assert.True(app.Outbox.Enabled);
            opId = Guid.NewGuid();
            id = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created, opId);
            Assert.Equal(1, CountOutbox(app.Db));
            var row = Assert.Single(app.Outbox.Pending());
            Assert.Equal(opId, row.OpId);
            Assert.Equal("homework", row.EntityType);
            Assert.Equal("upsert", row.Action);
            Assert.Equal(0, row.ExpectedRevision);
            Assert.Equal("pending", row.Status);
            Assert.NotEqual(Guid.Empty, row.EntityId);
            Assert.Equal(id, row.LocalRowId);
            Close(app);
        }

        using (var again = OpenAccount(dir.Root))
        {
            var hw = Assert.Single(again.Homework.GetAll());
            Assert.Equal(id, hw.Id);
            Assert.Equal("глава 1", hw.Text);
            Assert.Equal(1, CountOutbox(again.Db));
            var row = Assert.Single(again.Outbox.Pending());
            Assert.Equal(opId, row.OpId);
            Assert.Equal(hw.EntityId, row.EntityId);
        }
    }

    [Fact]
    public void Account_update_and_delete_write_outbox_in_the_same_commit()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        app.Homework.UpdateHomework(id, "глава 2", 2);
        Assert.Equal("глава 2", app.Homework.GetById(id)!.Text);
        Assert.Equal(2, app.Homework.GetById(id)!.TargetNthOccurrence);
        var pending = Assert.Single(app.Outbox.Pending());
        Assert.Equal("upsert", pending.Action);
        Assert.Equal("pending", pending.Status);

        AckLocal(app, pending, 1);
        app.Homework.Delete(id);
        Assert.Empty(app.Homework.GetAll());
        var deleted = Assert.Single(app.Outbox.Pending());
        Assert.Equal("delete", deleted.Action);
        Assert.Equal(1, deleted.ExpectedRevision);
        Assert.Equal(1, CountHomeworkRows(app.Db));
    }

    [Fact]
    public void Crash_before_commit_drops_both_domain_and_outbox()
    {
        using var dir = new ProfileTestDirectory();
        using (var app = OpenAccount(dir.Root))
        {
            app.Outbox.BeforeCommit = () => throw new InvalidOperationException("crash");
            Assert.Throws<InvalidOperationException>(() =>
                app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created));
            Assert.Empty(app.Homework.GetAll());
            Assert.Equal(0, CountOutbox(app.Db));
            Close(app);
        }

        using (var again = OpenAccount(dir.Root))
        {
            Assert.Empty(again.Homework.GetAll());
            Assert.Equal(0, CountOutbox(again.Db));
            Assert.Equal(0, CountHomeworkRows(again.Db));
        }
    }

    [Fact]
    public void Crash_during_update_keeps_previous_domain_and_outbox()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var opId = Guid.NewGuid();
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created, opId);
        app.Outbox.BeforeCommit = () => throw new InvalidOperationException("crash");
        Assert.Throws<InvalidOperationException>(() => app.Homework.UpdateHomework(id, "глава 2", 1));
        Assert.Equal("глава 1", app.Homework.GetById(id)!.Text);
        var row = Assert.Single(app.Outbox.Pending());
        Assert.Equal(opId, row.OpId);
        Assert.Equal("upsert", row.Action);
    }

    [Fact]
    public void Exact_opId_retry_does_not_double_apply_locally()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var opId = Guid.NewGuid();
        var first = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created, opId);
        var again = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created, opId);
        Assert.Equal(first, again);
        Assert.Single(app.Homework.GetAll());
        Assert.Equal(1, CountOutbox(app.Db));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            app.Homework.AddHomework("лек ИСТОРИЯ", "другой текст", 1, Created, opId));
        Assert.Equal("Повтор операции с другим содержимым.", ex.Message);
        Assert.Equal("глава 1", Assert.Single(app.Homework.GetAll()).Text);
        Assert.Equal(1, CountOutbox(app.Db));
    }

    [Fact]
    public void Homework_delete_cancels_pending_completion_in_the_same_savepoint()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var id = app.Homework.AddHomework("лек ИСТОРИЯ", "глава 1", 1, Created);
        app.Homework.MarkDone(id, true);
        var before = app.Outbox.Pending();
        Assert.Contains(before, r => r.EntityType == "homework" && r.Action == "upsert");
        Assert.Contains(before, r => r.EntityType == "completion" && r.Action == "upsert");
        var entityId = before.First(r => r.EntityType == "homework").EntityId;

        app.Outbox.BeforeCommit = () => throw new InvalidOperationException("crash");
        Assert.Throws<InvalidOperationException>(() => app.Homework.Delete(id));
        Assert.NotNull(app.Homework.GetById(id));
        Assert.Contains(app.Outbox.Pending(), r => r.EntityType == "completion" && r.EntityId == entityId && r.Action == "upsert");

        app.Outbox.BeforeCommit = null;
        app.Homework.Delete(id);
        Assert.Empty(app.Homework.GetAll());
        Assert.DoesNotContain(app.Outbox.Pending(), r => r.EntityType == "completion");
        Assert.DoesNotContain(app.Outbox.Pending(), r => r.EntityType == "homework" && r.Action == "upsert");
    }

    [Fact]
    public void Account_override_friend_and_settings_enqueue_guest_does_not()
    {
        using var guestDb = TestDb.Create();
        var before = CountOutbox(guestDb.Services.Db);
        guestDb.Services.Overrides.AddOrUpdate("лек ФИЗИКА", "global", "Физика", "заметка");
        guestDb.Services.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = "#4CC38A", Enabled = true });
        var gs = guestDb.Services.Db.GetSettings();
        gs.ParityInvert = true;
        guestDb.Services.Db.SaveSettings(gs);
        Assert.Equal(before, CountOutbox(guestDb.Services.Db));

        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        app.Overrides.AddOrUpdate("лек ФИЗИКА", "global", "Физика", "заметка");
        app.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = "#4CC38A", Enabled = true, MemberNames = "Иван" });
        var settings = app.Db.GetSettings();
        settings.MyGroupId = "3313";
        settings.ParityInvert = true;
        settings.NotifyTime1 = "08:00";
        settings.NotifyTime2 = "18:30";
        settings.IntersectionStrictness = 50;
        settings.AlwaysShowAllTrafficLights = true;
        app.Db.SaveSettings(settings);
        var types = app.Outbox.Pending().Select(r => r.EntityType).OrderBy(t => t).ToArray();
        Assert.Contains("override", types);
        Assert.Contains("friend", types);
        Assert.Contains("settings", types);
        Assert.All(app.Outbox.Pending(), r => Assert.Equal("upsert", r.Action));
    }

    internal static AppServices OpenAccount(string root)
    {
        var guest = AppServices.Create(root, () => false);
        guest.AllowNetwork = false;
        var account = guest.CreateProfile(ProfileDescriptor.Account(root, new AccountServerScope(ScopeUri).Key, UserId));
        account.AllowNetwork = false;
        guest.Dispose();
        return account;
    }

    internal static void Close(AppServices app)
    {
        SqliteConnection.ClearPool(app.Db.Connection);
        app.Dispose();
    }

    internal static int CountOutbox(Database db)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_outbox";
        return Convert.ToInt32((long)cmd.ExecuteScalar()!);
    }

    internal static long CountHomeworkRows(Database db)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM homework";
        return (long)cmd.ExecuteScalar()!;
    }

    internal static void AckLocal(AppServices app, PrivateSyncOutboxEntry row, long revision)
    {
        using var cmd = app.Db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE homework SET revision=@r WHERE entityUuid=@u";
        cmd.Parameters.AddWithValue("@r", revision);
        cmd.Parameters.AddWithValue("@u", row.EntityId.ToString("D"));
        cmd.ExecuteNonQuery();
        using var del = app.Db.Connection.CreateCommand();
        del.CommandText = "DELETE FROM sync_outbox WHERE opId=@id";
        del.Parameters.AddWithValue("@id", row.OpId.ToString("D"));
        del.ExecuteNonQuery();
    }
}
