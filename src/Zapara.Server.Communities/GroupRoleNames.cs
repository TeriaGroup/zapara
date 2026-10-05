namespace Zapara.Server.Communities;

public static class GroupRoleNames
{
    public static string? Clean(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length is < 2 or > 32 || name.Any(char.IsControl)) return null;
        var key = name.ToLowerInvariant();
        if (key is "староста" or "куратор" or "участник" or "member" or "headman" or "curator") return null;
        return name;
    }
}
