using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapara.Contracts.Sync;

internal sealed class SyncMutationConverter : JsonConverter<SyncMutation>
{
    public override SyncMutation Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        SyncJson.Fields(root, "syncEpoch", "opId", "entityType", "entityId", "expectedRevision", "action", "value");
        var entityType = root.GetProperty("entityType").GetString()!;
        return new(root.GetProperty("syncEpoch").GetGuid(), root.GetProperty("opId").GetGuid(), entityType,
            root.GetProperty("entityId").GetGuid(), root.GetProperty("expectedRevision").GetInt64(),
            root.GetProperty("action").GetString()!, SyncJson.Value(root.GetProperty("value"), entityType));
    }
    public override void Write(Utf8JsonWriter writer, SyncMutation value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("syncEpoch", value.SyncEpoch);
        writer.WriteString("opId", value.OpId);
        writer.WriteString("entityType", value.EntityType);
        writer.WriteString("entityId", value.EntityId);
        writer.WriteNumber("expectedRevision", value.ExpectedRevision);
        writer.WriteString("action", value.Action);
        SyncJson.Value(writer, value.Value);
        writer.WriteEndObject();
    }
}

internal sealed class SyncRecordConverter : JsonConverter<SyncRecord>
{
    public override SyncRecord Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        SyncJson.Fields(root, "entityType", "entityId", "revision", "tombstone", "changedAt", "value");
        var entityType = root.GetProperty("entityType").GetString()!;
        return new(entityType, root.GetProperty("entityId").GetGuid(), root.GetProperty("revision").GetInt64(),
            root.GetProperty("tombstone").GetBoolean(), root.GetProperty("changedAt").Deserialize<DateTimeOffset>(SyncJson.CreateOptions()),
            SyncJson.Value(root.GetProperty("value"), entityType));
    }
    public override void Write(Utf8JsonWriter writer, SyncRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("entityType", value.EntityType);
        writer.WriteString("entityId", value.EntityId);
        writer.WriteNumber("revision", value.Revision);
        writer.WriteBoolean("tombstone", value.Tombstone);
        writer.WriteString("changedAt", SyncJson.UtcText(value.ChangedAt));
        SyncJson.Value(writer, value.Value);
        writer.WriteEndObject();
    }
}
