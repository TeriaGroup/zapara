using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

public static class CommunityJson
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new CommunityUtcConverter() }
    };
    public static T Parse<T>(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > CommunityValidation.RequestBytes) throw CommunityValidation.Invalid();
        try
        {
            Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 16 });
            Unique(document.RootElement);
            return document.RootElement.Deserialize<T>(CreateOptions()) ?? throw CommunityValidation.Invalid();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        { throw CommunityValidation.Invalid(); }
    }
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CreateOptions());
    internal static string UtcText(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw CommunityValidation.Invalid();
                Unique(field.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Unique(child);
    }
}

internal sealed class CommunityUtcConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) || CommunityJson.UtcText(value) != text)
            throw CommunityValidation.Invalid();
        return value;
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(CommunityJson.UtcText(CommunityValidation.Utc(value)));
}
