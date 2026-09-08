using System.Globalization;
using System.Text;

namespace Zapara.Contracts.Sync;

public static class SyncValidation
{
    public static readonly Guid SettingsId = new("00000000-0000-0000-0000-000000000001");
    public const int ValueBytes = 32 * 1024;
    public const int RequestBytes = 64 * 1024;
    public const int RecordBytes = 36 * 1024;
    public const int PageRecords = 200;
    public const int PageBytes = 8 * 1024 * 1024;
    internal static ArgumentException Invalid() => new("Недопустимый контракт Sync.");
    public static string Text(string? value, int maximum)
    {
        if (value is null || value.Length > maximum * 2) throw Invalid();
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0' || char.IsLowSurrogate(c)) throw Invalid();
            if (char.IsHighSurrogate(c) && (++i >= value.Length || !char.IsLowSurrogate(value[i]))) throw Invalid();
            if (++count > maximum) throw Invalid();
        }
        return value;
    }
    internal static string? Optional(string? value, int maximum) => value is null ? null : Text(value, maximum);
    internal static Guid Id(Guid id) => id != Guid.Empty ? id : throw Invalid();
    internal static long Nonnegative(long value) => value >= 0 ? value : throw Invalid();
    internal static int Range(int value, int min, int max) => value >= min && value <= max ? value : throw Invalid();
    internal static DateTimeOffset Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw Invalid();
    public static string NormalizeSubject(string raw)
    {
        Text(raw, 256);
        return string.Join(" ", raw.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
    internal static string SubjectKey(string raw, string key)
        => Text(key, 256) == NormalizeSubject(raw) ? key : throw Invalid();
    internal static string? Time(string? value) => value is null ? null :
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? value : throw Invalid();
    internal static string Scope(string value) => value == "global" ||
        (value is { Length: 9 } && value.StartsWith("weekday:", StringComparison.Ordinal) && value[8] is >= '1' and <= '7')
        ? value : throw Invalid();
    internal static void Identity(string type, Guid id)
    {
        Id(id);
        if (type is not ("homework" or "completion" or "override" or "friend" or "settings") ||
            (type == "settings" && id != SettingsId)) throw Invalid();
    }
    internal static void Value(string type, SyncValue? value, bool deleted)
    {
        if (deleted) { if (value is not null) throw Invalid(); return; }
        var matches = (type, value) switch
        {
            ("homework", HomeworkValue) or ("completion", CompletionValue) or ("override", OverrideValue)
                or ("friend", FriendValue) or ("settings", SettingsValue) => true,
            _ => false
        };
        if (!matches || SyncJson.ValueUtf8(value!).Length > ValueBytes) throw Invalid();
    }
}
