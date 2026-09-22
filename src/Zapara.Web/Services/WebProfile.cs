using System.Text.Json;
using System.Text.Json.Serialization;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public sealed class WebProfile
{
    public string Owner { get; set; } = "guest";
    public long StorageRevision { get; set; }
    public long ResetEpoch { get; set; }
    public Dictionary<string, LocalEntity> Records { get; set; } = [];
    public List<PendingChange> Outbox { get; set; } = [];
    public Dictionary<string, TransferReceipt> ImportReceipts { get; set; } = [];
    public Guid? SyncEpoch { get; set; }
    public long AfterSequence { get; set; }
    public bool HasSnapshot { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}

public sealed class LocalEntity
{
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public long Revision { get; set; }
    public bool Tombstone { get; set; }
    public JsonElement? Value { get; set; }
}

public sealed class PendingChange
{
    public Guid OpId { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public long ExpectedRevision { get; set; }
    public string Action { get; set; } = "upsert";
    public JsonElement? Value { get; set; }
    public Guid? SyncEpoch { get; set; }
    public string Status { get; set; } = "pending";
    public SyncRecord? ServerRecord { get; set; }
    public SyncRecord? RelatedServerRecord { get; set; }
    public string? ConflictCode { get; set; }
}

public sealed record DevicePreferences
{
    [JsonIgnore] public string ActiveOwner { get; set; } = "guest";
    [JsonIgnore] public string? AccountName { get; set; }
    public string Theme { get; set; } = "system";
    public bool Animations { get; set; } = true;
    public bool AlphaMaps { get; set; }
    public bool Notifications { get; set; }
}

public sealed record EntityValue<T>(Guid Id, long Revision, T Value);
public sealed record ProfileChange(string EntityType, Guid EntityId, SyncValue? Value)
{
    public Guid OpId { get; init; } = Guid.NewGuid();
}
public sealed record CachedIdentity(string Owner, string? Name, string? SessionGeneration = null);

public static class ProfileValues
{
    public static readonly SettingsValue DefaultSettings = new(null, false, "20:00", "07:30", 25, false);
    public static string Key(string type, Guid id) => type + ":" + id.ToString("D");
    public static T? Read<T>(LocalEntity entity) => entity.Tombstone || entity.Value is null ? default :
        entity.Value.Value.Deserialize<T>(SyncJson.CreateOptions());
    public static JsonElement Serialize(SyncValue value) => JsonSerializer.SerializeToElement(value, value.GetType(), SyncJson.CreateOptions());
}
