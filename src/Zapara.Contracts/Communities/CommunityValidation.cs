using System.Buffers;
using System.Text;

namespace Zapara.Contracts.Communities;

public static class CommunityValidation
{
    public const int RequestBytes = 64 * 1024;
    internal static ArgumentException Invalid() => new("Недопустимый контракт сообщества.");
    public static Guid Id(Guid value) => value != Guid.Empty ? value : throw Invalid();
    public static DateTimeOffset Utc(DateTimeOffset value)
        => value != default && value.Offset == TimeSpan.Zero ? value : throw Invalid();
    public static long Revision(long value) => value >= 0 ? value : throw Invalid();
    public static string Role(string? value) => value is "member" or "headman" or "curator" ? value : throw Invalid();
    public static string StaffRole(string? value) => value is "headman" or "curator" ? value : throw Invalid();
    public static string JoinStatus(string? value) => value is "pending" or "accepted" or "rejected" ? value : throw Invalid();
    public static string GroupId(string? value) => Text(value, 64);
    public static string Name(string? value) => Text(value, 80);
    public static string Description(string? value) => value is null ? "" : Text(value, 2000, true);
    public static string Title(string? value) => Text(value, 200);
    public static string Body(string? value) => Text(value, 8000);
    public static string Question(string? value) => Text(value, 400);
    public static string Option(string? value) => Text(value, 80);
    public static IReadOnlyList<string> Options(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count is < 2 or > 16) throw Invalid();
        var options = new string[values.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < values.Count; i++)
        {
            options[i] = Option(values[i]);
            if (!unique.Add(options[i])) throw Invalid();
        }
        return Array.AsReadOnly(options);
    }
    public static string Text(string? value, int maximum, bool allowEmpty = false)
    {
        if (value is null || value.Length > maximum * 2) throw Invalid();
        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done ||
                rune.Value == 0 || Rune.IsControl(rune)) throw Invalid();
            remaining = remaining[consumed..];
            count++;
        }
        if (count > maximum || (count == 0 && !allowEmpty)) throw Invalid();
        return value;
    }
}
