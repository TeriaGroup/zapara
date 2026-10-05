using Vograph.Core.Services.Sync;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed class SyncConflictDecisionTests
{
    private static readonly Guid EntityId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid NewOpId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static HomeworkValue LocalHomework(string text = "локальный черновик")
        => new("лек ИСТОРИЯ", "лек история", text, 1, Now, null);

    private static SyncRecord ServerHomework(long revision = 4, bool tombstone = false, string text = "серверная версия",
        DateTimeOffset? changedAt = null)
        => new("homework", EntityId, revision, tombstone,
            changedAt ?? Now,
            tombstone ? null : new HomeworkValue("лек ИСТОРИЯ", "лек история", text, 1, Earlier, null));

    [Fact]
    public void KeepLocal_uses_caller_opId_and_server_revision_never_auto_lww()
    {
        var local = LocalHomework();
        var olderServer = ServerHomework(revision: 7, text: "старее по времени", changedAt: Earlier);
        var newerLocalTime = LocalHomework("новее локально");

        var decision = SyncConflictDecision.KeepLocal("homework", EntityId, newerLocalTime, olderServer, NewOpId);

        Assert.Equal(SyncConflictKind.KeepLocal, decision.Kind);
        Assert.Equal("homework", decision.EntityType);
        Assert.Equal(EntityId, decision.EntityId);
        Assert.Same(newerLocalTime, decision.LocalValue);
        Assert.Same(olderServer, decision.ServerRecord);
        Assert.Equal(NewOpId, decision.NewOpId);
        Assert.Equal(7, decision.ExpectedRevision);
        Assert.Equal("upsert", decision.Action);
        Assert.False(decision.DropDraft);
        Assert.False(decision.AbortQueuedMutation);
        // Timestamps must not drive the outcome: local is newer, server wins only if chosen.
        Assert.True(newerLocalTime.CreatedAtUtc > olderServer.ChangedAt);
    }

    [Fact]
    public void KeepLocal_delete_draft_against_live_or_tombstone_uses_server_revision()
    {
        var live = ServerHomework(revision: 3, tombstone: false);
        var tomb = ServerHomework(revision: 5, tombstone: true);

        var againstLive = SyncConflictDecision.KeepLocal("homework", EntityId, localValue: null, live, NewOpId);
        Assert.Equal("delete", againstLive.Action);
        Assert.Equal(3, againstLive.ExpectedRevision);
        Assert.Null(againstLive.LocalValue);

        var againstTomb = SyncConflictDecision.KeepLocal("homework", EntityId, localValue: null, tomb, NewOpId);
        Assert.Equal("delete", againstTomb.Action);
        Assert.Equal(5, againstTomb.ExpectedRevision);
    }

    [Fact]
    public void KeepServer_drops_local_draft_without_new_op()
    {
        var local = LocalHomework();
        var server = ServerHomework(revision: 9, tombstone: true);

        var decision = SyncConflictDecision.KeepServer("homework", EntityId, local, server);

        Assert.Equal(SyncConflictKind.KeepServer, decision.Kind);
        Assert.Equal(EntityId, decision.EntityId);
        Assert.Same(local, decision.LocalValue);
        Assert.Same(server, decision.ServerRecord);
        Assert.Null(decision.NewOpId);
        Assert.Null(decision.ExpectedRevision);
        Assert.Null(decision.Action);
        Assert.True(decision.DropDraft);
        Assert.False(decision.AbortQueuedMutation);
    }

    [Fact]
    public void Expired410_aborts_queued_mutation_and_does_not_replay()
    {
        var decision = SyncConflictDecision.Expired410("homework", EntityId);

        Assert.Equal(SyncConflictKind.Expired410, decision.Kind);
        Assert.Equal("homework", decision.EntityType);
        Assert.Equal(EntityId, decision.EntityId);
        Assert.Null(decision.LocalValue);
        Assert.Null(decision.ServerRecord);
        Assert.Null(decision.NewOpId);
        Assert.Null(decision.ExpectedRevision);
        Assert.Null(decision.Action);
        Assert.True(decision.AbortQueuedMutation);
        Assert.True(decision.DropDraft);
    }

    [Fact]
    public void Diagnostic_methods_return_stable_loc_keys_not_russian_copy()
    {
        var conflict = SyncConflictDecision.KeepLocal("homework", EntityId, LocalHomework(), ServerHomework(), NewOpId);
        Assert.Equal("syncConflictBody", conflict.ConflictBodyKey());
        Assert.Equal("syncKeepLocal", conflict.KeepLocalKey());
        Assert.Equal("syncKeepServer", conflict.KeepServerKey());
        Assert.Equal("syncExpired", conflict.ExpiredKey());
        Assert.Equal("syncConflictBody", conflict.DiagnosticKey());

        var expired = SyncConflictDecision.Expired410("override", EntityId);
        Assert.Equal("syncExpired", expired.DiagnosticKey());
        Assert.Equal("syncExpired", expired.ExpiredKey());
    }

    [Fact]
    public void KeepLocal_rejects_mismatched_identity_or_empty_opId()
    {
        var server = ServerHomework();
        var otherId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        Assert.ThrowsAny<ArgumentException>(() =>
            SyncConflictDecision.KeepLocal("completion", EntityId, LocalHomework(), server, NewOpId));
        Assert.ThrowsAny<ArgumentException>(() =>
            SyncConflictDecision.KeepLocal("homework", otherId, LocalHomework(), server, NewOpId));
        Assert.ThrowsAny<ArgumentException>(() =>
            SyncConflictDecision.KeepLocal("homework", EntityId, LocalHomework(), server, Guid.Empty));
    }

    [Fact]
    public void No_automatic_last_write_wins_factory_exists()
    {
        var type = typeof(SyncConflictDecision);
        Assert.Null(type.GetMethod("LastWriteWins"));
        Assert.Null(type.GetMethod("AutoResolve"));
        Assert.Null(type.GetMethod("ResolveByTimestamp"));
        var names = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(SyncConflictDecision.KeepLocal), names);
        Assert.Contains(nameof(SyncConflictDecision.KeepServer), names);
        Assert.Contains(nameof(SyncConflictDecision.Expired410), names);
        Assert.DoesNotContain("LastWriteWins", names);
    }
}
