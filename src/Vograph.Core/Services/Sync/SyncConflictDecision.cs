using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public enum SyncConflictKind
{
    KeepLocal,
    KeepServer,
    Expired410
}

/// <summary>
/// Explicit conflict outcome for private sync. Never auto last-write-wins by timestamp.
/// Loc copy lives under stable keys; I18nService owns the Russian values.
/// </summary>
public sealed class SyncConflictDecision
{
    private SyncConflictDecision(
        SyncConflictKind kind,
        string entityType,
        Guid entityId,
        SyncValue? localValue,
        SyncRecord? serverRecord,
        Guid? newOpId,
        long? expectedRevision,
        string? action,
        bool dropDraft,
        bool abortQueuedMutation)
    {
        Kind = kind;
        EntityType = entityType;
        EntityId = entityId;
        LocalValue = localValue;
        ServerRecord = serverRecord;
        NewOpId = newOpId;
        ExpectedRevision = expectedRevision;
        Action = action;
        DropDraft = dropDraft;
        AbortQueuedMutation = abortQueuedMutation;
    }

    public SyncConflictKind Kind { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public SyncValue? LocalValue { get; }
    public SyncRecord? ServerRecord { get; }
    public Guid? NewOpId { get; }
    public long? ExpectedRevision { get; }
    public string? Action { get; }
    public bool DropDraft { get; }
    public bool AbortQueuedMutation { get; }

    public static SyncConflictDecision KeepLocal(
        string entityType, Guid entityId, SyncValue? localValue, SyncRecord serverRecord, Guid newOpId)
    {
        ArgumentNullException.ThrowIfNull(serverRecord);
        RequireIdentity(entityType, entityId, serverRecord);
        if (newOpId == Guid.Empty) throw new ArgumentException("Новый opId обязателен.", nameof(newOpId));
        return new(
            SyncConflictKind.KeepLocal,
            entityType,
            entityId,
            localValue,
            serverRecord,
            newOpId,
            serverRecord.Revision,
            localValue is null ? "delete" : "upsert",
            dropDraft: false,
            abortQueuedMutation: false);
    }

    public static SyncConflictDecision KeepServer(
        string entityType, Guid entityId, SyncValue? localValue, SyncRecord serverRecord)
    {
        ArgumentNullException.ThrowIfNull(serverRecord);
        RequireIdentity(entityType, entityId, serverRecord);
        return new(
            SyncConflictKind.KeepServer,
            entityType,
            entityId,
            localValue,
            serverRecord,
            newOpId: null,
            expectedRevision: null,
            action: null,
            dropDraft: true,
            abortQueuedMutation: false);
    }

    public static SyncConflictDecision Expired410(string entityType, Guid entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Тип сущности обязателен.", nameof(entityType));
        if (entityId == Guid.Empty) throw new ArgumentException("Идентификатор сущности обязателен.", nameof(entityId));
        return new(
            SyncConflictKind.Expired410,
            entityType,
            entityId,
            localValue: null,
            serverRecord: null,
            newOpId: null,
            expectedRevision: null,
            action: null,
            dropDraft: true,
            abortQueuedMutation: true);
    }

    public string ConflictBodyKey() => "syncConflictBody";
    public string KeepLocalKey() => "syncKeepLocal";
    public string KeepServerKey() => "syncKeepServer";
    public string ExpiredKey() => "syncExpired";
    public string DiagnosticKey() => Kind == SyncConflictKind.Expired410 ? ExpiredKey() : ConflictBodyKey();

    public override string ToString() => "SyncConflictDecision { [REDACTED] }";

    private static void RequireIdentity(string entityType, Guid entityId, SyncRecord serverRecord)
    {
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Тип сущности обязателен.", nameof(entityType));
        if (entityId == Guid.Empty) throw new ArgumentException("Идентификатор сущности обязателен.", nameof(entityId));
        if (!string.Equals(entityType, serverRecord.EntityType, StringComparison.Ordinal) ||
            entityId != serverRecord.EntityId)
            throw new ArgumentException("Локальный черновик и серверная запись относятся к разным сущностям.");
    }
}
