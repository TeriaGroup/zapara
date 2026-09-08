using System.Text.Json.Serialization;

namespace Zapara.Contracts.Sync;

public sealed record SyncMetadata
{
    [JsonConstructor]
    public SyncMetadata(Guid syncEpoch, long currentSequence, long minAfterSequence)
    {
        SyncEpoch = SyncValidation.Id(syncEpoch);
        CurrentSequence = SyncValidation.Nonnegative(currentSequence);
        MinAfterSequence = minAfterSequence >= 0 && minAfterSequence <= currentSequence ? minAfterSequence : throw SyncValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public Guid SyncEpoch { get; private init; }
    [JsonRequired, JsonInclude] public long CurrentSequence { get; private init; }
    [JsonRequired, JsonInclude] public long MinAfterSequence { get; private init; }
}

/// <summary>Frozen domain outcomes; transport and receipt execution are deferred.</summary>
public sealed record SyncMutationResult
{
    [JsonConstructor]
    public SyncMutationResult(int status, string code, SyncMetadata metadata, SyncRecord? serverRecord)
    {
        var valid = (status, code) switch
        {
            (200, "applied") => serverRecord is not null,
            (409, "revision_conflict") => true,
            (409, "creation_metadata_immutable") => serverRecord is { Tombstone: false },
            (409, "op_id_reused" or "friend_limit") or (410, "sync_reset") => serverRecord is null,
            _ => false
        };
        if (!valid) throw SyncValidation.Invalid();
        Status = status;
        Code = code;
        Metadata = metadata ?? throw SyncValidation.Invalid();
        ServerRecord = serverRecord;
    }
    [JsonRequired, JsonInclude] public int Status { get; private init; }
    [JsonRequired, JsonInclude] public string Code { get; private init; }
    [JsonRequired, JsonInclude] public SyncMetadata Metadata { get; private init; }
    [JsonRequired, JsonInclude] public SyncRecord? ServerRecord { get; private init; }
    public override string ToString() => "SyncMutationResult { [REDACTED] }";
}

public sealed record SyncChange
{
    [JsonConstructor]
    public SyncChange(long sequence, Guid opId, SyncRecord record)
    {
        Sequence = sequence > 0 ? sequence : throw SyncValidation.Invalid();
        OpId = SyncValidation.Id(opId);
        Record = record ?? throw SyncValidation.Invalid();
        if (record.Revision > sequence) throw SyncValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public long Sequence { get; private init; }
    [JsonRequired, JsonInclude] public Guid OpId { get; private init; }
    [JsonRequired, JsonInclude] public SyncRecord Record { get; private init; }
    public override string ToString() => "SyncChange { [REDACTED] }";
}

public sealed record SyncChangesPage
{
    [JsonConstructor]
    public SyncChangesPage(SyncMetadata metadata, long afterSequence, long nextAfterSequence, bool hasMore, IReadOnlyList<SyncChange> changes)
    {
        Metadata = metadata ?? throw SyncValidation.Invalid();
        if (changes is null || changes.Count > SyncValidation.PageRecords || afterSequence < metadata.MinAfterSequence ||
            afterSequence > metadata.CurrentSequence) throw SyncValidation.Invalid();
        var previous = afterSequence;
        foreach (var change in changes)
        {
            if (change is null || change.Sequence <= previous || change.Sequence > metadata.CurrentSequence) throw SyncValidation.Invalid();
            previous = change.Sequence;
        }
        if (nextAfterSequence != previous || (hasMore && (changes.Count == 0 || previous >= metadata.CurrentSequence))) throw SyncValidation.Invalid();
        (AfterSequence, NextAfterSequence, HasMore, Changes) = (afterSequence, nextAfterSequence, hasMore, Array.AsReadOnly(changes.ToArray()));
        if (SyncJson.Serialize(this).Length > SyncValidation.PageBytes) throw SyncValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public SyncMetadata Metadata { get; private init; }
    [JsonRequired, JsonInclude] public long AfterSequence { get; private init; }
    [JsonRequired, JsonInclude] public long NextAfterSequence { get; private init; }
    [JsonRequired, JsonInclude] public bool HasMore { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<SyncChange> Changes { get; private init; }
    public override string ToString() => "SyncChangesPage { [REDACTED] }";
}

public sealed record SyncResyncManifest
{
    [JsonConstructor]
    public SyncResyncManifest(Guid manifestId, Guid syncEpoch, long highWater, DateTimeOffset createdAt, DateTimeOffset expiresAt, long itemCount)
    {
        ManifestId = SyncValidation.Id(manifestId);
        SyncEpoch = SyncValidation.Id(syncEpoch);
        HighWater = SyncValidation.Nonnegative(highWater);
        CreatedAt = SyncValidation.Utc(createdAt);
        ExpiresAt = SyncValidation.Utc(expiresAt);
        if (expiresAt - createdAt != TimeSpan.FromMinutes(10)) throw SyncValidation.Invalid();
        ItemCount = SyncValidation.Nonnegative(itemCount);
    }
    [JsonRequired, JsonInclude] public Guid ManifestId { get; private init; }
    [JsonRequired, JsonInclude] public Guid SyncEpoch { get; private init; }
    [JsonRequired, JsonInclude] public long HighWater { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset CreatedAt { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset ExpiresAt { get; private init; }
    [JsonRequired, JsonInclude] public long ItemCount { get; private init; }
}

public sealed record SyncManifestItem
{
    [JsonConstructor]
    public SyncManifestItem(long ordinal, SyncRecord record)
        => (Ordinal, Record) = (ordinal > 0 ? ordinal : throw SyncValidation.Invalid(), record ?? throw SyncValidation.Invalid());
    [JsonRequired, JsonInclude] public long Ordinal { get; private init; }
    [JsonRequired, JsonInclude] public SyncRecord Record { get; private init; }
    public override string ToString() => "SyncManifestItem { [REDACTED] }";
}

public sealed record SyncResyncPage
{
    [JsonConstructor]
    public SyncResyncPage(SyncResyncManifest manifest, long afterOrdinal, long nextAfterOrdinal, bool hasMore, IReadOnlyList<SyncManifestItem> items)
    {
        Manifest = manifest ?? throw SyncValidation.Invalid();
        if (items is null || items.Count > SyncValidation.PageRecords || afterOrdinal < 0 || afterOrdinal > manifest.ItemCount) throw SyncValidation.Invalid();
        var previous = afterOrdinal;
        foreach (var item in items)
        {
            if (item is null || previous == long.MaxValue || item.Ordinal != previous + 1 ||
                item.Ordinal > manifest.ItemCount || item.Record.Revision > manifest.HighWater) throw SyncValidation.Invalid();
            previous = item.Ordinal;
        }
        if (nextAfterOrdinal != previous || hasMore != (previous < manifest.ItemCount) || (hasMore && items.Count == 0)) throw SyncValidation.Invalid();
        (AfterOrdinal, NextAfterOrdinal, HasMore, Items) = (afterOrdinal, nextAfterOrdinal, hasMore, Array.AsReadOnly(items.ToArray()));
        if (SyncJson.Serialize(this).Length > SyncValidation.PageBytes) throw SyncValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public SyncResyncManifest Manifest { get; private init; }
    [JsonRequired, JsonInclude] public long AfterOrdinal { get; private init; }
    [JsonRequired, JsonInclude] public long NextAfterOrdinal { get; private init; }
    [JsonRequired, JsonInclude] public bool HasMore { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<SyncManifestItem> Items { get; private init; }
    public override string ToString() => "SyncResyncPage { [REDACTED] }";
}

public sealed record SyncError
{
    [JsonConstructor]
    public SyncError(int status, string code)
    {
        if ((status, code) is not ((400, "invalid_request" or "invalid_cursor") or (401, "invalid_session") or
            (410, "sync_reset" or "manifest_expired") or (413, "payload_too_large") or
            (429, "rate_limited") or (503, "db_unavailable"))) throw SyncValidation.Invalid();
        (Status, Code) = (status, code);
    }
    [JsonRequired, JsonInclude] public int Status { get; private init; }
    [JsonRequired, JsonInclude] public string Code { get; private init; }
}
