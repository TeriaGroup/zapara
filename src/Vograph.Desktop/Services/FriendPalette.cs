namespace Vograph.Desktop.Services;

/// <summary>Five friend colors. DB keeps the hex; the UI draws Brush.Friend{index+1} so both themes look right.</summary>
public static class FriendPalette
{
    public static readonly string[] Hex = { "#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C" };

    // WPF 1.x palette → nearest new slot (blue, green, red→pink, violet, yellow→orange).
    private static readonly Dictionary<string, int> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#6CA5E0"] = 2, ["#98C379"] = 1, ["#E06C75"] = 4, ["#C678DD"] = 3, ["#F2C55C"] = 0,
    };

    public static int IndexOf(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return 0;
        var h = hex.Trim().ToUpperInvariant();
        if (h.Length == 9 && h[0] == '#') h = "#" + h[3..]; // #AARRGGBB → #RRGGBB
        var i = Array.FindIndex(Hex, x => x.Equals(h, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        return Legacy.TryGetValue(h, out var legacy) ? legacy : 0;
    }
}
