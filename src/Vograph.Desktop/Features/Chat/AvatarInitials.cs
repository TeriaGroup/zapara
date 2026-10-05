using System.Text;

namespace Vograph.Desktop.Features.Chat;

public static class AvatarInitials
{
    public static string FromName(string? name)
    {
        var words = (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.EnumerateRunes().Where(Rune.IsLetterOrDigit).Take(2).ToArray())
            .Where(runes => runes.Length > 0).ToArray();
        if (words.Length == 0) return "?";
        var first = words[0][0].ToString();
        var second = words.Length > 1 ? words[^1][0].ToString()
            : words[0].Length > 1 ? words[0][1].ToString() : "";
        return (first + second).ToUpperInvariant();
    }
}
