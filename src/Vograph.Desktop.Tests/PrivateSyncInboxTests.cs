using Vograph.Core.Services.Sync;
using Zapara.Contracts.Sync;
using Xunit;
using static Vograph.Desktop.Tests.PrivateSyncOutboxTests;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
    private static HomeworkValue Value(string text) => new("Математика", "математика", text, 1, Now, null);
    private static void Snapshot(PrivateSyncOutbox box, Guid epoch, params SyncRecord[] records)
    {
        var high = records.Length == 0 ? 0 : records.Max(r => r.Revision);
        var manifest = new SyncResyncManifest(Guid.NewGuid(), epoch, high, Now, Now.AddMinutes(10), records.Length);
        box.BeginSnapshot(manifest);
        box.StageSnapshot(new(manifest, 0, records.Length, false, records.Select((r, i) => new SyncManifestItem(i + 1, r)).ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_publication_or_late_profile_invalidation_preserves_visible_data_cursor_and_stage(bool invalidate)
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        var id = Guid.NewGuid();
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", id, 1, false, Now, Value("old")));
        app.Outbox.PublishSnapshot();
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", id, 2, false, Now, Value("new")));
        var valid = true;
        app.Outbox.BeforeCommit = () => { if (invalidate) valid = false; else throw new IOException("disk full"); };
        Assert.ThrowsAny<Exception>(() => app.Outbox.PublishSnapshot(() => { if (!valid) throw new OperationCanceledException(); }));
        Assert.Equal("old", Assert.Single(app.Homework.GetAll()).Text);
        Assert.Equal(1, app.Outbox.AfterSequence);
        Assert.NotNull(app.Outbox.SnapshotManifest);
        Assert.Equal(1, app.Outbox.SnapshotAfterOrdinal);
        app.Outbox.BeforeCommit = null;
        app.Outbox.PublishSnapshot();
        Assert.Equal("new", Assert.Single(app.Homework.GetAll()).Text);
        Assert.Equal(2, app.Outbox.AfterSequence);
    }

    [Fact]
    public void Remote_edit_of_dirty_row_records_conflict_and_never_overwrites_local_text()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        var id = Guid.NewGuid();
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", id, 1, false, Now, Value("base")));
        app.Outbox.PublishSnapshot();
        app.Homework.UpdateHomework(app.Homework.GetAll()[0].Id, "local", 1);
        app.Outbox.ApplyChanges(new(new(epoch, 2, 0), 1, 2, false,
            [new(2, Guid.NewGuid(), new("homework", id, 2, false, Now, Value("remote")))]));
        Assert.Equal("local", app.Homework.GetAll()[0].Text);
        Assert.Equal("conflict", Assert.Single(app.Outbox.Pending()).Status);
        Assert.Single(app.Outbox.Drafts());
        Assert.Equal("remote", ((HomeworkValue)app.Outbox.RemoteRecord("homework", id)!.Value!).Text);
        Assert.Equal(2, app.Outbox.AfterSequence);
    }

    [Fact]
    public void Own_committed_change_acknowledges_exact_lost_receipt_without_false_conflict()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        Snapshot(app.Outbox, epoch);
        app.Outbox.PublishSnapshot();
        app.Homework.AddHomework("Математика", "local", 1, Now.UtcDateTime);
        var row = app.Outbox.Pending().Single();
        var request = app.Outbox.BuildMutation(row, epoch)!;
        app.Outbox.ApplyChanges(new(new(epoch, 1, 0), 0, 1, false,
            [new(1, row.OpId, new("homework", row.EntityId, 1, false, Now, request.Value))]));
        Assert.Empty(app.Outbox.Pending());
        Assert.Empty(app.Outbox.Drafts());
        Assert.Equal(1, app.Homework.GetAll()[0].Revision);
    }

    [Fact]
    public void Reset_removes_only_known_synced_rows_preserving_pending_local_draft()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        Snapshot(app.Outbox, Guid.NewGuid(), new SyncRecord("homework", Guid.NewGuid(), 1, false, Now, Value("server")));
        app.Outbox.PublishSnapshot();
        app.Homework.AddHomework("Математика", "offline", 1, Now.UtcDateTime);
        var pending = Assert.Single(app.Outbox.Pending());
        Snapshot(app.Outbox, Guid.NewGuid());
        app.Outbox.PublishSnapshot();
        Assert.Equal("offline", Assert.Single(app.Homework.GetAll()).Text);
        Assert.Equal(pending.OpId, Assert.Single(app.Outbox.Pending()).OpId);
    }

    [Fact]
    public void Creation_metadata_change_rolls_back_entire_incoming_page()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        var id = Guid.NewGuid();
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", id, 1, false, Now, Value("original")));
        app.Outbox.PublishSnapshot();
        var bad = new HomeworkValue("Математика", "математика", "invalid", 1, Now.AddSeconds(1), null);
        Assert.Throws<InvalidOperationException>(() => app.Outbox.ApplyChanges(new(new(epoch, 3, 0), 1, 3, false,
            [new(2, Guid.NewGuid(), new("friend", Guid.NewGuid(), 2, false, Now, new FriendValue(null, "О732Б", "Друг", 1, true))),
             new(3, Guid.NewGuid(), new("homework", id, 3, false, Now, bad))])));
        Assert.Empty(app.Db.GetFriends());
        Assert.Equal("original", app.Homework.GetAll()[0].Text);
        Assert.Equal(1, app.Outbox.AfterSequence);
    }

    [Fact]
    public void Parent_tombstone_preserves_local_completion_as_conflict_not_invisible_pending_work()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        var id = Guid.NewGuid();
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", id, 1, false, Now, Value("task")));
        app.Outbox.PublishSnapshot();
        app.Homework.MarkDone(app.Homework.GetAll()[0].Id, true);
        app.Outbox.ApplyChanges(new(new(epoch, 2, 0), 1, 2, false, [new(2, Guid.NewGuid(), new("homework", id, 2, true, Now, null))]));
        Assert.Equal("done", Assert.Single(app.Homework.GetAll()).Status);
        Assert.Equal("conflict", Assert.Single(app.Outbox.Pending()).Status);
    }

    [Fact]
    public void Full_snapshot_with_lost_receipt_keeps_exact_operation_retryable()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        Snapshot(app.Outbox, epoch);
        app.Outbox.PublishSnapshot();
        app.Homework.AddHomework("Математика", "sent", 1, Now.UtcDateTime);
        var pending = Assert.Single(app.Outbox.Pending());
        var request = app.Outbox.BuildMutation(pending, epoch)!;
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", pending.EntityId, 1, false, Now, request.Value));
        app.Outbox.PublishSnapshot();
        var retry = Assert.Single(app.Outbox.Pending());
        Assert.Equal(pending.OpId, retry.OpId);
        Assert.Equal("pending", retry.Status);
        Assert.Empty(app.Outbox.Drafts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stale_dialog_cannot_overwrite_a_new_edit_or_another_epoch(bool switchEpoch)
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        app.Outbox.SetEpoch(epoch, 0);
        var localId = app.Homework.AddHomework("Математика", "draft", 1, Now.UtcDateTime);
        var row = app.Outbox.Pending().Single();
        var local = app.Outbox.BuildMutation(row, epoch)!.Value;
        var server = new SyncRecord("homework", row.EntityId, 2, false, Now, Value("remote"));
        app.Outbox.MarkConflict(row, new(409, "revision_conflict", new(epoch, 2, 0), server));
        var decision = SyncConflictDecision.KeepServer("homework", row.EntityId, local, server);
        if (switchEpoch) app.Outbox.SetEpoch(Guid.NewGuid(), 0);
        else app.Homework.UpdateHomework(localId, "new draft", 2);
        Assert.False(app.Outbox.ResolveConflict(decision, epoch));
        Assert.Equal(switchEpoch ? "draft" : "new draft", app.Homework.GetById(localId)!.Text);
        Assert.NotEmpty(app.Outbox.Pending());
    }

    [Fact]
    public void Keeping_completion_after_parent_delete_recreates_linked_homework_and_completion_with_new_identity()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var epoch = Guid.NewGuid();
        var id = Guid.NewGuid();
        Snapshot(app.Outbox, epoch, new SyncRecord("homework", id, 1, false, Now, Value("task")));
        app.Outbox.PublishSnapshot();
        app.Homework.MarkDone(app.Homework.GetAll()[0].Id, true);
        app.Outbox.ApplyChanges(new(new(epoch, 2, 0), 1, 2, false, [new(2, Guid.NewGuid(), new("homework", id, 2, true, Now, null))]));
        var row = Assert.Single(app.Outbox.Pending());
        var local = app.Outbox.BuildMutation(row, epoch)!.Value;
        var decision = SyncConflictDecision.KeepLocal("completion", id, local, new("completion", id, 2, true, Now, null), Guid.NewGuid());
        Assert.True(app.Outbox.ResolveConflict(decision, epoch));
        var hw = Assert.Single(app.Homework.GetAll());
        Assert.NotEqual(id, hw.EntityId);
        Assert.Equal("done", hw.Status);
        Assert.Equal(0, hw.Revision);
        Assert.Equal(2, app.Outbox.Pending().Count);
        Assert.All(app.Outbox.Pending(), p => { Assert.Equal(hw.EntityId, p.EntityId); Assert.Equal(0, p.ExpectedRevision); });
        Assert.Empty(app.Outbox.Drafts());
    }
}
