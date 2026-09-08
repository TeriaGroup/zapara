using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace Zapara.Contracts.Accounts;

public static class AccountValidation
{
    public static string Username(string? value)
    {
        if (value is null || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]{3,32}\z", RegexOptions.CultureInvariant))
            throw Invalid();
        return value;
    }

    public static string NormalizeUsername(string value) => Username(value).ToLowerInvariant();

    public static string Password(string? value)
    {
        Scalars(value, 12, 128, false);
        return value!;
    }

    public static string? DisplayName(string? value)
    {
        if (value is not null) Scalars(value, 1, 80, true);
        return value;
    }

    public static string DeviceName(string? value)
    {
        Scalars(value, 1, 80, true);
        return value!;
    }

    public static Guid Id(Guid value) => value != Guid.Empty ? value : throw Invalid();
    public static string Platform(string? value) => value is "windows" or "android" ? value : throw Invalid();
    public static DateTimeOffset Utc(DateTimeOffset value)
        => value != default && value.Offset == TimeSpan.Zero ? value : throw Invalid();

    public static string Token(string? value, string prefix)
    {
        if (value is null || value.Length != 46 || !value.StartsWith(prefix, StringComparison.Ordinal)) throw Invalid();
        var encoded = value[3..];
        if (!Regex.IsMatch(encoded, @"\A[A-Za-z0-9_-]{43}\z", RegexOptions.CultureInvariant)) throw Invalid();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/') + "="); }
        catch (FormatException) { throw Invalid(); }
        if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != encoded) throw Invalid();
        return value;
    }

    public static string Email(string? value)
    {
        if (value is null || value.Length is < 6 or > 254) throw Invalid();
        if (!Regex.IsMatch(value, @"\A[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,24}\z", RegexOptions.CultureInvariant))
            throw Invalid();
        return value.ToLowerInvariant();
    }

    private static void Scalars(string? value, int minimum, int maximum, bool rejectControls)
    {
        if (value is null || value.Length > maximum * 2) throw Invalid();
        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done ||
                rune.Value == 0 || (rejectControls && Rune.IsControl(rune))) throw Invalid();
            remaining = remaining[consumed..];
            count++;
        }
        if (count < minimum || count > maximum) throw Invalid();
    }

    private static ArgumentException Invalid() => new("Недопустимые данные аккаунта.");
}
