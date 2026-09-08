using System.Globalization;
using System.Text.Json;
using Vograph.Core.Models;

namespace Vograph.Core.Services;

internal static class TimetableApiJson
{
    internal static void Require(bool condition)
    {
        if (!condition) throw new TimetableApiException(TimetableApiFailure.InvalidPayload);
    }

    internal static JsonElement Object(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) Require(names.Add(property.Name));
        return value;
    }

    internal static JsonElement Field(JsonElement obj, string name)
    {
        Require(obj.TryGetProperty(name, out var value));
        return value;
    }

    internal static string Text(JsonElement obj, string name, int max = 2048, bool nonempty = false)
    {
        var field = Field(obj, name);
        Require(field.ValueKind == JsonValueKind.String);
        var value = field.GetString()!;
        Require(value.Length <= max && (!nonempty || !string.IsNullOrWhiteSpace(value)));
        return value;
    }

    internal static string? NullableText(JsonElement obj, string name, int max = 2048)
        => Field(obj, name).ValueKind == JsonValueKind.Null ? null : Text(obj, name, max);

    internal static int Integer(JsonElement obj, string name)
    {
        var field = Field(obj, name);
        Require(field.ValueKind == JsonValueKind.Number);
        Require(field.TryGetInt32(out var value));
        return value;
    }

    internal static bool Boolean(JsonElement obj, string name)
    {
        var field = Field(obj, name);
        Require(field.ValueKind is JsonValueKind.True or JsonValueKind.False);
        return field.GetBoolean();
    }

    internal static Guid Uuid(JsonElement obj, string name)
    {
        Require(Guid.TryParseExact(Text(obj, name, 36), "D", out var value) && value != Guid.Empty);
        return value;
    }

    internal static DateTimeOffset Timestamp(JsonElement obj, string name)
    {
        var text = Text(obj, name, 40);
        // Explicit UTC offset is mandatory; do not infer the machine's local zone.
        Require(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal));
        Require(Field(obj, name).TryGetDateTimeOffset(out var value) && value.Offset == TimeSpan.Zero);
        return value;
    }

    internal static DateTimeOffset? NullableTimestamp(JsonElement obj, string name)
        => Field(obj, name).ValueKind == JsonValueKind.Null ? null : Timestamp(obj, name);

    internal static JsonElement Array(JsonElement obj, string name, int max)
    {
        var value = Field(obj, name);
        Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= max);
        return value;
    }

    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 64 }
        && !string.IsNullOrWhiteSpace(id) && id == id.Trim() && !id.Any(char.IsControl);

    internal static TimeOnly Time(JsonElement obj, string name)
    {
        var text = Text(obj, name, 5);
        Require(text.Length == 5 && TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _));
        return TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);
    }
}
