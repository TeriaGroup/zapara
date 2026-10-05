using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Zapara.Server.Social;

public static class ChatCards
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Canonical(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4000) throw new SocialException(400, "invalid_request");
        JsonDocument document;
        try { document = JsonDocument.Parse(raw); }
        catch (JsonException) { throw new SocialException(400, "invalid_request"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String)
                throw new SocialException(400, "invalid_request");
            if (root.TryGetProperty("v", out var version) && (version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1))
                throw new SocialException(400, "invalid_request");
            var json = kind.GetString() switch
            {
                "schedule" => Schedule(root),
                "lesson" => Lesson(root),
                "homework" => Homework(root),
                "tasks" => Tasks(root),
                "place" => Place(root),
                _ => throw new SocialException(400, "invalid_request")
            };
            if (json.Length is < 2 or > 2000) throw new SocialException(400, "invalid_request");
            return json;
        }
    }

    public static string Preview(string? body)
    {
        try
        {
            using var document = JsonDocument.Parse(body ?? "");
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            return type switch
            {
                "schedule" => Join("Расписание", Text(root, "title", 80, false)),
                "lesson" => Join("Пара", Text(root, "subject", 80, false)),
                "homework" => Join("Домашка", Text(root, "subject", 80, false)),
                "tasks" => "Домашка",
                "place" => Join("Аудитория", Text(root, "room", 40, false).Length > 0 ? Text(root, "room", 40, false) : Text(root, "building", 16, false)),
                _ => "Карточка"
            };
        }
        catch (Exception) { return "Карточка"; }
    }

    private static string Schedule(JsonElement root)
    {
        Expect(root, "type", "date", "group", "title", "items", "v");
        var items = Items(root, 8);
        if (items.Count == 0) throw new SocialException(400, "invalid_request");
        return Write(new
        {
            v = 1,
            type = "schedule",
            date = Date(root),
            group = Text(root, "group", 32, false),
            title = Text(root, "title", 80, true),
            items
        });
    }

    private static string Lesson(JsonElement root)
    {
        Expect(root, "type", "date", "time", "lesson", "subject", "place", "teacher", "group", "v");
        return Write(new
        {
            v = 1,
            type = "lesson",
            date = OptionalDate(root),
            time = Text(root, "time", 24, true),
            lesson = Text(root, "lesson", 16, false),
            subject = Text(root, "subject", 80, true),
            place = Text(root, "place", 40, false),
            teacher = Text(root, "teacher", 60, false),
            group = Text(root, "group", 32, false)
        });
    }

    private static string Homework(JsonElement root)
    {
        Expect(root, "type", "subject", "text", "done", "v");
        return Write(new
        {
            v = 1,
            type = "homework",
            subject = Text(root, "subject", 80, true),
            text = Text(root, "text", 500, true),
            done = Flag(root)
        });
    }

    private static string Tasks(JsonElement root)
    {
        Expect(root, "type", "items", "v");
        if (!root.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 6)
            throw new SocialException(400, "invalid_request");
        var items = new List<object>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new SocialException(400, "invalid_request");
            Expect(item, "subject", "text");
            items.Add(new { subject = Text(item, "subject", 80, true), text = Text(item, "text", 240, true) });
        }
        return Write(new { v = 1, type = "tasks", items });
    }

    private static string Place(JsonElement root)
    {
        Expect(root, "type", "building", "floor", "room", "subject", "v");
        var building = Text(root, "building", 16, false);
        var room = Text(root, "room", 40, false);
        if (building.Length == 0 && room.Length == 0) throw new SocialException(400, "invalid_request");
        return Write(new
        {
            v = 1,
            type = "place",
            building,
            floor = Text(root, "floor", 8, false),
            room,
            subject = Text(root, "subject", 80, false)
        });
    }

    private static List<object> Items(JsonElement root, int max)
    {
        if (!root.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array) throw new SocialException(400, "invalid_request");
        var items = new List<object>();
        foreach (var item in array.EnumerateArray())
        {
            if (items.Count == max) break;
            if (item.ValueKind != JsonValueKind.Object) throw new SocialException(400, "invalid_request");
            Expect(item, "time", "lesson", "subject", "place", "teacher");
            var subject = Text(item, "subject", 80, true);
            items.Add(new
            {
                time = Text(item, "time", 24, true),
                lesson = Text(item, "lesson", 16, false),
                subject,
                place = Text(item, "place", 40, false),
                teacher = Text(item, "teacher", 60, false)
            });
        }
        return items;
    }

    private static void Expect(JsonElement root, params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name)) throw new SocialException(400, "invalid_request");
    }

    private static string Date(JsonElement root)
    {
        var value = Text(root, "date", 10, true);
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new SocialException(400, "invalid_request");
        return value;
    }

    private static string OptionalDate(JsonElement root)
    {
        var value = Text(root, "date", 10, false);
        if (value.Length == 0) return "";
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new SocialException(400, "invalid_request");
        return value;
    }

    private static bool Flag(JsonElement root)
    {
        if (!root.TryGetProperty("done", out var value)) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        throw new SocialException(400, "invalid_request");
    }

    private static string Text(JsonElement root, string name, int max, bool required)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
            return required ? throw new SocialException(400, "invalid_request") : "";
        if (value.ValueKind != JsonValueKind.String) throw new SocialException(400, "invalid_request");
        var text = (value.GetString() ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Any(char.IsControl)) throw new SocialException(400, "invalid_request");
        if (text.Length > max) text = text[..max];
        if (required && text.Length == 0) throw new SocialException(400, "invalid_request");
        return text;
    }

    private static string Join(string label, string detail) => detail.Length == 0 ? label : label + " · " + detail;

    private static string Write(object value) => JsonSerializer.Serialize(value, Json);
}
