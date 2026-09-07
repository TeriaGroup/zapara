namespace Vograph.Desktop.Dialogs;

/// <summary>Group numbers mix Cyrillic letters with digits; users often type the Latin look-alike (A for А, C for С).</summary>
public static class GroupSearch
{
    private static readonly Dictionary<char, char> LatinToCyrillic = new()
    {
        ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['E'] = 'Е', ['H'] = 'Н', ['K'] = 'К',
        ['M'] = 'М', ['O'] = 'О', ['P'] = 'Р', ['T'] = 'Т', ['X'] = 'Х', ['Y'] = 'У',
    };

    public static string Normalize(string s) => NormalizeChars(s.Trim());

    /// <summary>Per-character normalisation (upper case + Latin look-alikes) that keeps positions: MatchRange maps back into the original name.</summary>
    private static string NormalizeChars(string s)
    {
        var chars = s.ToUpperInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (LatinToCyrillic.TryGetValue(chars[i], out var cyr)) chars[i] = cyr;
        return new string(chars);
    }

    public static bool Matches(string name, string query)
    {
        var q = Normalize(query);
        return q.Length == 0 || NormalizeChars(name).Contains(q, StringComparison.Ordinal);
    }

    /// <summary>Where the query sits inside the name, as indices into the original string; null without a match or a query.</summary>
    public static (int Start, int Length)? MatchRange(string name, string query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return null;
        var at = NormalizeChars(name).IndexOf(q, StringComparison.Ordinal);
        return at < 0 ? null : (at, q.Length);
    }
}
