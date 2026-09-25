namespace Zapara.Server.Communities;

public static class GroupTopicNames
{
    public const string GeneralTitle = "Общий";
    public const string GeneralIcon = "💬";

    public const string ChatKind = "chat";
    public const string BallotsKind = "ballots";

    public static string? Kind(string? raw) => raw switch
    {
        null or "" or ChatKind => ChatKind,
        BallotsKind => BallotsKind,
        _ => null
    };

    public const string DefaultAccent = "default";
    public const string AllWriters = "all";
    public const string ManagersOnly = "managers";

    public static string? Description(string? raw)
    {
        var description = (raw ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return description.Length <= 240 && !description.Any(value => char.IsControl(value) && value != '\n') ? description : null;
    }

    public static string? Accent(string? raw) => raw switch
    {
        null or "" or DefaultAccent => DefaultAccent,
        "blue" or "green" or "purple" or "orange" or "red" => raw,
        _ => null
    };

    public static string? WritePolicy(string? raw) => raw switch
    {
        null or "" or AllWriters => AllWriters,
        ManagersOnly => ManagersOnly,
        _ => null
    };

    public static string? Title(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length is < 2 or > 40 || name.Any(char.IsControl)) return null;
        if (name.Equals(GeneralTitle, StringComparison.OrdinalIgnoreCase)) return null;
        return name;
    }

    public static string? Icon(string? raw)
    {
        var icon = (raw ?? "").Trim();
        if (icon.Length == 0) return GeneralIcon;
        if (icon.Length > 8 || icon.Any(char.IsControl)) return null;
        return icon;
    }
}
