using System.Text.Json;

namespace Zapara.Server.Communities;

public sealed record GroupChange(string Kind, Guid RoleId, Guid UserId, string Name, string Power, bool Enabled);

public static class GroupChanges
{
    public static readonly string[] Powers = ["joins", "exclude", "roles", "grants", "ballots", "close"];

    public static bool KnownPower(string? power) => power is "joins" or "exclude" or "roles" or "grants" or "ballots" or "close";

    public static string PowerTitle(string power) => power switch
    {
        "joins" => "Принимать заявки",
        "exclude" => "Исключать участников",
        "roles" => "Менять роли",
        "grants" => "Назначать роли",
        "ballots" => "Объявлять голосование",
        "close" => "Завершать голосования",
        _ => ""
    };

    public static bool Passes(int yes, int no, int need) => need >= 1 && yes >= need && yes > no;

    public static string? Canonical(string? kind, Guid roleId, Guid userId, string? name, string? power, bool enabled)
    {
        var cleaned = GroupRoleNames.Clean(name);
        var payload = kind switch
        {
            "power" when roleId != Guid.Empty && KnownPower(power) && string.IsNullOrWhiteSpace(name) && userId == Guid.Empty
                => $"{{\"kind\":\"power\",\"roleId\":\"{roleId:D}\",\"power\":\"{power}\",\"enabled\":{(enabled ? "true" : "false")}}}",
            "grant" or "revoke_grant" when roleId != Guid.Empty && userId != Guid.Empty && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(power)
                => $"{{\"kind\":\"{kind}\",\"roleId\":\"{roleId:D}\",\"userId\":\"{userId:D}\"}}",
            "create_role" when cleaned is not null && roleId == Guid.Empty && userId == Guid.Empty && string.IsNullOrWhiteSpace(power)
                => "{\"kind\":\"create_role\",\"name\":" + Json(cleaned) + "}",
            "rename_role" when cleaned is not null && roleId != Guid.Empty && userId == Guid.Empty && string.IsNullOrWhiteSpace(power)
                => $"{{\"kind\":\"rename_role\",\"roleId\":\"{roleId:D}\",\"name\":{Json(cleaned)}}}",
            "delete_role" when roleId != Guid.Empty && userId == Guid.Empty && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(power)
                => $"{{\"kind\":\"delete_role\",\"roleId\":\"{roleId:D}\"}}",
            "remove_member" when userId != Guid.Empty && roleId == Guid.Empty && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(power)
                => $"{{\"kind\":\"remove_member\",\"userId\":\"{userId:D}\"}}",
            _ => null
        };
        return payload is { Length: >= 2 and <= 800 } ? payload : null;
    }

    public static GroupChange? Read(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!keys.Add(property.Name)) return null;
            var kind = Text(root, "kind");
            var expected = kind switch
            {
                "power" => new[] { "kind", "roleId", "power", "enabled" },
                "grant" or "revoke_grant" => new[] { "kind", "roleId", "userId" },
                "create_role" => new[] { "kind", "name" },
                "rename_role" => new[] { "kind", "roleId", "name" },
                "delete_role" => new[] { "kind", "roleId" },
                "remove_member" => new[] { "kind", "userId" },
                _ => null
            };
            if (expected is null || keys.Count != expected.Length || expected.Any(key => !keys.Contains(key))) return null;
            var roleId = keys.Contains("roleId") ? GuidOf(root, "roleId") : Guid.Empty;
            var userId = keys.Contains("userId") ? GuidOf(root, "userId") : Guid.Empty;
            var name = keys.Contains("name") ? Text(root, "name") ?? "" : "";
            var power = keys.Contains("power") ? Text(root, "power") ?? "" : "";
            var enabled = keys.Contains("enabled") && root.GetProperty("enabled").ValueKind == JsonValueKind.True;
            if (keys.Contains("enabled") && root.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            if (kind is "create_role" or "rename_role" && GroupRoleNames.Clean(name) != name) return null;
            if (kind == "power" && (!KnownPower(power) || roleId == Guid.Empty)) return null;
            if (kind is "grant" or "revoke_grant" && (roleId == Guid.Empty || userId == Guid.Empty)) return null;
            if (kind == "delete_role" && roleId == Guid.Empty) return null;
            if (kind == "remove_member" && userId == Guid.Empty) return null;
            return new(kind!, roleId, userId, kind is "create_role" or "rename_role" ? name : "", power, enabled);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public static string? Question(GroupChange change, string? roleName, string? personName)
    {
        var role = (roleName ?? "").Trim();
        var person = (personName ?? "").Trim();
        var title = PowerTitle(change.Power);
        return change.Kind switch
        {
            "power" when role.Length > 0 && title.Length > 0 && change.Enabled => $"Дать роли «{role}» возможность «{title}»?",
            "power" when role.Length > 0 && title.Length > 0 => $"Забрать у роли «{role}» возможность «{title}»?",
            "grant" when role.Length > 0 && person.Length > 0 => $"Назначить {person} роль «{role}»?",
            "revoke_grant" when role.Length > 0 && person.Length > 0 => $"Снять с {person} роль «{role}»?",
            "create_role" when change.Name.Length > 0 => $"Создать роль «{change.Name}»?",
            "rename_role" when role.Length > 0 && change.Name.Length > 0 => $"Переименовать роль «{role}» в «{change.Name}»?",
            "delete_role" when role.Length > 0 => $"Удалить роль «{role}»?",
            "remove_member" when person.Length > 0 => $"Исключить {person} из группы?",
            _ => null
        };
    }

    private static string Json(string value)
    {
        var encoded = new System.Text.StringBuilder(value.Length + 2);
        encoded.Append('"');
        foreach (var ch in value)
        {
            if (ch is < ' ') return "\"\"";
            if (ch is '\\' or '"') encoded.Append('\\');
            encoded.Append(ch);
        }
        encoded.Append('"');
        return encoded.ToString();
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Guid GuidOf(JsonElement root, string name)
    {
        var text = Text(root, name);
        return Guid.TryParseExact(text, "D", out var id) ? id : Guid.Empty;
    }
}
