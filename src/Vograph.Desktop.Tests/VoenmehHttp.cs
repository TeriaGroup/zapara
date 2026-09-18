namespace Vograph.Desktop.Tests;

internal static class VoenmehHttp
{
    public const string Meta = """
        {"has_data":true,"updated_at":"2026-09-16T12:00:00Z","period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["А863С","09С31","Е452Б"]}
        """;

    public const string OldMeta = """
        {"has_data":true,"updated_at":"2026-09-01T10:00:00Z","period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["А863С"]}
        """;

    public const string PhilosophyLessons = """
        {"lessons":[{"day":3,"time":"9:00","week":"odd","kind":"лек","subject":"ФИЛОСОФИЯ","teachers":[],"rooms":["526*"]}]}
        """;

    public const string MathJsonLessons = """
        {"lessons":[{"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"ВЫСШ. МАТ.","teachers":["Барт Е.Л."],"rooms":["493"]}]}
        """;

    public static FakeHttpHandler Handler(string meta = Meta, string? a863 = PhilosophyLessons, string empty = """{"lessons":[]}""")
        => new()
        {
            Respond = r =>
            {
                var uri = r.RequestUri ?? throw new InvalidOperationException("missing uri");
                if (uri.AbsolutePath.EndsWith("/meta", StringComparison.Ordinal))
                    return FakeHttpHandler.Text(meta);
                if (uri.AbsolutePath.Contains("/lessons", StringComparison.Ordinal))
                {
                    if (uri.Query.Contains(Uri.EscapeDataString("А863С"), StringComparison.Ordinal))
                        return FakeHttpHandler.Text(a863 ?? empty);
                    return FakeHttpHandler.Text(empty);
                }
                throw new InvalidOperationException(uri.ToString());
            }
        };
}
