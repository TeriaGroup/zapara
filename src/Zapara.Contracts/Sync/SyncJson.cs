using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapara.Contracts.Sync;

public static class SyncJson
{
    public const string DigestVersion = "zapara.sync.mutation.v1";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new SyncUtcConverter(), new SyncDateConverter() }
    };
    public static T Parse<T>(ReadOnlySpan<byte> bytes)
    {
        var maximum = typeof(T) == typeof(SyncRecord) ? SyncValidation.RecordBytes :
            typeof(T) == typeof(SyncChangesPage) || typeof(T) == typeof(SyncResyncPage) ? SyncValidation.PageBytes : SyncValidation.RequestBytes;
        if (bytes.Length > maximum) throw SyncValidation.Invalid();
        try
        {
            Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 16 });
            Unique(document.RootElement);
            return document.RootElement.Deserialize<T>(CreateOptions()) ?? throw SyncValidation.Invalid();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        { throw SyncValidation.Invalid(); }
    }
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CreateOptions());
    public static byte[] Digest(SyncMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return SHA256.HashData(Utf8.GetBytes(DigestVersion + "\n").Concat(Serialize(mutation)).ToArray());
    }
    internal static byte[] ValueUtf8(SyncValue value) => JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), CreateOptions());
    internal static SyncValue? Value(JsonElement element, string type)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        var options = CreateOptions();
        return type switch
        {
            "homework" => element.Deserialize<HomeworkValue>(options),
            "completion" => element.Deserialize<CompletionValue>(options),
            "override" => element.Deserialize<OverrideValue>(options),
            "friend" => element.Deserialize<FriendValue>(options),
            "settings" => element.Deserialize<SettingsValue>(options),
            _ => throw SyncValidation.Invalid()
        };
    }
    internal static void Value(Utf8JsonWriter writer, SyncValue? value)
    {
        writer.WritePropertyName("value");
        if (value is null) writer.WriteNullValue();
        else JsonSerializer.Serialize(writer, value, value.GetType(), CreateOptions());
    }
    internal static void Fields(JsonElement element, params string[] fields)
    {
        Unique(element);
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != fields.Length ||
            fields.Any(f => !element.TryGetProperty(f, out _))) throw SyncValidation.Invalid();
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject())
            {
                SyncValidation.Text(field.Name, SyncValidation.RequestBytes);
                if (!names.Add(field.Name)) throw SyncValidation.Invalid();
                Unique(field.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Unique(child);
        else if (element.ValueKind == JsonValueKind.String)
            SyncValidation.Text(element.GetString(), SyncValidation.RequestBytes);
    }
    internal static string UtcText(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}

internal sealed class SyncUtcConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) || SyncJson.UtcText(value) != text)
            throw SyncValidation.Invalid();
        return value;
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(SyncJson.UtcText(SyncValidation.Utc(value)));
}

internal sealed class SyncDateConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => DateOnly.TryParseExact(reader.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value : throw SyncValidation.Invalid();
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
