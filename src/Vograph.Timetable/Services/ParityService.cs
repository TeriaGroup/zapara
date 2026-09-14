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
}
