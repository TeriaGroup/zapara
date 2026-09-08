namespace Vograph.Core.Services;

public static class ParityService
{
    // Spec: week containing September 1 is week 1 = odd. Alternate thereafter.
    // Studs.js: start = Period StartYear/Month/Day, align to Monday of that week, then weeks = ceil((today - monday)/86400000) /7
    public static bool IsOddWeek(DateTime date, DateTime periodStart, int weekCount, bool invert)
    {
        // Align periodStart to Monday
        var start = periodStart.Date;
        int dow = (int)start.DayOfWeek;
        if (dow == 0) dow = 7; // Sunday 0 -> 7
        var monday = start.AddDays(-(dow - 1)).Date; // Monday of week containing Sep 1

        var target = date.Date;
        int days = (int)Math.Ceiling((target - monday).TotalDays);
        if (days < 1) days = 1;
        int weekCode = (int)Math.Ceiling(days / 7.0) % weekCount;
        if (weekCode == 0) weekCode = weekCount;
        bool isOdd = weekCode == 1;
        return invert ? !isOdd : isOdd;
    }

    public static int GetWeekCode(DateTime date, DateTime periodStart, int weekCount)
    {
        var start = periodStart.Date;
        int dow = (int)start.DayOfWeek;
        if (dow == 0) dow = 7;
        var monday = start.AddDays(-(dow - 1)).Date;
        int days = (int)Math.Ceiling((date.Date - monday).TotalDays);
        if (days < 1) days = 1;
        int weekCode = (int)Math.Ceiling(days / 7.0) % weekCount;
        if (weekCode == 0) weekCode = weekCount;
        return weekCode;
    }

    public static int GetWeekNumber(DateTime date, DateTime periodStart)
    {
        var start = periodStart.Date;
        int dow = (int)start.DayOfWeek;
        if (dow == 0) dow = 7;
        var monday = start.AddDays(-(dow - 1)).Date;
        int days = (int)Math.Ceiling((date.Date - monday).TotalDays);
        if (days < 1) days = 1;
        int weekNumber = (int)Math.Ceiling(days / 7.0);
        if (weekNumber < 1) weekNumber = 1;
        return weekNumber;
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
