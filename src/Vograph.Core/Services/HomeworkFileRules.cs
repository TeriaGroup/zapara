namespace Vograph.Core.Services;

public sealed class HomeworkFileException : Exception
{
    public HomeworkFileException(string code) : base(code) => Code = code;
    public string Code { get; }
}

public static class HomeworkFileRules
{
    public const int MaxFiles = 6;
    public const int PhotoBytes = 12 * 1024 * 1024;
    public const int DocumentBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "txt", "csv", "rtf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "zip"
    };
    private static readonly HashSet<string> Photos = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "gif" };

    public static string CleanName(string raw)
    {
        var baseName = raw.Replace('\\', '/').Split('/').Last().Trim();
        var cleaned = new string(baseName.Where(ch => ch >= ' ' && ch != '"' && ch != '\'').ToArray());
        if (cleaned.Length == 0 || cleaned.Contains("..", StringComparison.Ordinal)) return "";
        return cleaned.Length <= 80 ? cleaned : cleaned[..80];
    }

    public static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[(dot + 1)..].ToLowerInvariant();
    }

    public static string Accept(string kind, string rawName, int size)
    {
        if (size <= 0) throw new HomeworkFileException("bad");
        var name = CleanName(rawName);
        if (name.Length == 0 || name.Count(ch => ch == '.') != 1) throw new HomeworkFileException("bad");
        var ext = ExtensionOf(name);
        return kind switch
        {
            "photo" when size > PhotoBytes => throw new HomeworkFileException("big"),
            "photo" when Photos.Contains(ext) => name,
            "document" when size > DocumentBytes => throw new HomeworkFileException("big"),
            "document" when Documents.Contains(ext) => name,
            _ => throw new HomeworkFileException("bad")
        };
    }
}
