using System.Globalization;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public static class GroupCardLogic
{
    /// <summary>Chip text when the timetable is older than 3 days; Warn when older than 7.</summary>
    /// <summary>R2-01: плашка в карточке группы — только когда копия правда старая (больше 3 дней), с датой копии
    /// («Расписание от 21 сент.»). Предупреждающая — после 7 дней или если источник сообщает, что данные устарели.
    /// Свежая копия плашку не показывает, даже если сервер отметил источник устаревшим (раньше она висела всегда).</summary>
    public static (string? Text, bool Warn) Stale(string? lastFetchedAt, DateTime utcNow, Loc loc, bool sourceStale = false)
    {
        if (!DateTime.TryParse(lastFetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last))
            return sourceStale ? (loc.T("scheduleMaybeOld"), true) : (null, false);
        var age = utcNow - last.ToUniversalTime();
        if (age.TotalDays <= 3) return (null, false);
        return (loc.T("scheduleFromChip", CopyDate(last.ToLocalTime())), age.TotalDays > 7 || sourceStale);
    }

    /// <summary>Дата копии расписания словами: «21 сент.».</summary>
    public static string CopyDate(DateTime local) => local.ToString("d MMM", CultureInfo.GetCultureInfo("ru-RU"));

    /// <summary>What the 64px rail can show of a group number: its first two characters («А863С» → «А8»); «—» without a group.</summary>
    public static string RailLabel(string? name)
    {
        var n = name?.Trim() ?? "";
        return n.Length == 0 ? "—" : n.Length <= 2 ? n : n[..2];
    }
}
