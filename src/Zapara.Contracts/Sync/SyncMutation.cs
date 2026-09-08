using System.Text.Json.Serialization;

namespace Zapara.Contracts.Sync;

[JsonConverter(typeof(SyncMutationConverter))]
public sealed record SyncMutation
{
    public SyncMutation(Guid syncEpoch, Guid opId, string entityType, Guid entityId, long expectedRevision, string action, SyncValue? value)
    {
        SyncEpoch = SyncValidation.Id(syncEpoch);
        OpId = SyncValidation.Id(opId);
        SyncValidation.Identity(entityType, entityId);
        EntityType = entityType;
        EntityId = entityId;
        ExpectedRevision = SyncValidation.Nonnegative(expectedRevision);
        Action = action is "upsert" or "delete" ? action : throw SyncValidation.Invalid();
        if (action == "delete" && expectedRevision == 0) throw SyncValidation.Invalid();
        SyncValidation.Value(entityType, value, action == "delete");
        Value = value;
    }
    public Guid SyncEpoch { get; }
    public Guid OpId { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public long ExpectedRevision { get; }
    public string Action { get; }
    public SyncValue? Value { get; }
    public override string ToString() => "SyncMutation { [REDACTED] }";
}

[JsonConverter(typeof(SyncRecordConverter))]
public sealed record SyncRecord
{
    public SyncRecord(string entityType, Guid entityId, long revision, bool tombstone, DateTimeOffset changedAt, SyncValue? value)
    {
        SyncValidation.Identity(entityType, entityId);
        EntityType = entityType;
        EntityId = entityId;
        Revision = revision > 0 ? revision : throw SyncValidation.Invalid();
        Tombstone = tombstone;
        ChangedAt = SyncValidation.Utc(changedAt);
        SyncValidation.Value(entityType, value, tombstone);
        Value = value;
        if (SyncJson.Serialize(this).Length > SyncValidation.RecordBytes) throw SyncValidation.Invalid();
    }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public long Revision { get; }
    public bool Tombstone { get; }
    public DateTimeOffset ChangedAt { get; }
    public SyncValue? Value { get; }
    public override string ToString() => "SyncRecord { [REDACTED] }";
}
