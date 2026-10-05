namespace Vograph.Core.Services;

public static class ParityService
{
    // Week containing 1 September is week 1 = odd. Later weeks are Monday–Sunday, odd then even.
    public static bool IsOddWeek(DateTime date, DateTime periodStart, int weekCount, bool invert)
    {
        bool isOdd = GetWeekCode(date, periodStart, weekCount) == 1;
        return invert ? !isOdd : isOdd;
    }

    public static int GetWeekCode(DateTime date, DateTime periodStart, int weekCount)
    {
        int weekCode = GetWeekNumber(date, periodStart) % weekCount;
        if (weekCode == 0) weekCode = weekCount;
        return weekCode;
    }

    public static int GetWeekNumber(DateTime date, DateTime periodStart)
    {
        var start = periodStart.Date;
        int dow = (int)start.DayOfWeek;
        if (dow == 0) dow = 7;
        var monday = start.AddDays(-(dow - 1)).Date;
        int days = (int)(date.Date - monday).TotalDays;
        if (days < 0) return 1;
        return days / 7 + 1;
    }

    public static string NormalizeSubject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim().ToLowerInvariant().Replace('ё', 'е');
        // collapse whitespace
        s = string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return s;
    }

    /// <summary>Letters and digits of <see cref="NormalizeSubject"/> — XML «лек ВЫСШ. МАТЕМАТ» and JSON «лек ВЫСШ. МАТ.» share a prefix.</summary>
    public static string SubjectMatchKey(string? raw)
    {
        var n = NormalizeSubject(raw ?? "");
        if (n.Length == 0) return "";
        var chars = n.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "" : new string(chars);
    }

    public static bool SameSubject(string? a, string? b)
    {
        var x = SubjectMatchKey(a);
        var y = SubjectMatchKey(b);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x == y) return true;
        var n = Math.Min(x.Length, y.Length);
        return n >= 8 && (x.StartsWith(y, StringComparison.Ordinal) || y.StartsWith(x, StringComparison.Ordinal));
    }

    public static int DayTitleToNumber(string title)
    {
        var t = title.Trim().ToLowerInvariant();
        return t switch
        {
            "понедельник" => 1,
            "вторник" => 2,
            "среда" => 3,
            "четверг" => 4,
            "пятница" => 5,
            "суббота" => 6,
            "воскресенье" => 7,
            _ => 0
        };
    }

    public static string DayNumberToTitle(int n) => n switch
    {
        1 => "Понедельник",
        2 => "Вторник",
        3 => "Среда",
        4 => "Четверг",
        5 => "Пятница",
        6 => "Суббота",
        7 => "Воскресенье",
        _ => ""
    };

    // WeekCode 1/2 wins. Empty/0/invalid falls back to Time ("9:00 Нечетная").
    public static int ParseXmlParity(string? weekCode, string? timeRaw)
    {
        if (int.TryParse(weekCode?.Trim(), out var fromCode) && fromCode is 1 or 2) return fromCode;
        var t = (timeRaw ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Contains("нечетн")) return 1;
        if (t.Contains("четн")) return 2;
        return 0;
    }
}
