using System.Globalization;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

public static class DayTitles
{
    public static string Title(int offset, DateTime date, Loc loc) => offset switch
    {
        -1 => loc.T("yesterday"),
        0 => loc.T("today"),
        1 => loc.T("tomorrow"),
        _ => loc.I18n.FormatDayFull(date)
    };

    public static string Subtitle(DateTime date, bool isOdd, int weekNumber, int lessonCount, Loc loc)
    {
        var en = loc.Language == "en";
        var culture = CultureInfo.GetCultureInfo(en ? "en-US" : "ru-RU");
        var dateText = date.ToString(en ? "MMMM d" : "d MMMM", culture);
        var count = lessonCount == 0 ? loc.T("noLessonsShort") : loc.Plural(lessonCount, "lessons1", "lessons2", "lessons5");
        return string.Join(" · ",
            $"{loc.I18n.FormatDayFull(date)}, {dateText}",
            loc.T("parityWeek", loc.I18n.FormatParity(isOdd)),
            loc.T("weekOf", weekNumber),
            count);
    }

    public static string TypeLabel(string typeRaw, Loc loc) => typeRaw.Trim().ToLowerInvariant() switch
    {
        "лек" => loc.T("typeLek"),
        "пр" => loc.T("typePr"),
        "лаб" => loc.T("typeLab"),
        "конс" => loc.T("typeKons"),
        "зач" => loc.T("typeZach"),
        "экз" => loc.T("typeEkz"),
        "курс" => loc.T("typeKurs"),
        "практика" => loc.T("typePraktika"),
        "" => "",
        var other => other
    };

    public static string ShortDate(DateTime d, Loc loc) => d.ToString(loc.Language == "en" ? "MM-dd" : "dd.MM");
}
