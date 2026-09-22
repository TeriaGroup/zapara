namespace Zapara.Server.Communities;

public static class GroupTopicNames
{
    public const string GeneralTitle = "Общий";
    public const string GeneralIcon = "💬";

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
