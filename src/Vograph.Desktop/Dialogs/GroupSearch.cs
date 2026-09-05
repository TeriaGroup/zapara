namespace Vograph.Desktop.Dialogs;

/// <summary>Group numbers mix Cyrillic letters with digits; users often type the Latin look-alike (A for А, C for С).</summary>
public static class GroupSearch
{
    private static readonly Dictionary<char, char> LatinToCyrillic = new()
    {
        ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['E'] = 'Е', ['H'] = 'Н', ['K'] = 'К',
        ['M'] = 'М', ['O'] = 'О', ['P'] = 'Р', ['T'] = 'Т', ['X'] = 'Х', ['Y'] = 'У',
    };

    public static string Normalize(string s)
    {
        var chars = s.Trim().ToUpperInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (LatinToCyrillic.TryGetValue(chars[i], out var cyr)) chars[i] = cyr;
        return new string(chars);
    }

    public static bool Matches(string name, string query)
    {
        var q = Normalize(query);
        return q.Length == 0 || Normalize(name).Contains(q, StringComparison.Ordinal);
    }
}
