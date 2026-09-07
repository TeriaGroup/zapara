using Vograph.Core.Models;
using System.Text;

namespace Vograph.Core.Services;

public class NotificationService
{
    private readonly Database _db;
    private readonly OverrideService _overrideService;
    private readonly HomeworkService _homeworkService;
    private readonly ScheduleService _scheduleService;
    private readonly I18nService _i18n;

    public NotificationService(Database db, OverrideService ov, HomeworkService hw, ScheduleService sched, I18nService i18n)
    {
        _db = db;
        _overrideService = ov;
        _homeworkService = hw;
        _scheduleService = sched;
        _i18n = i18n;
    }

    public string BuildNotificationText(DateTime date)
    {
        var settings = _db.GetSettings();
        if (string.IsNullOrEmpty(settings.MyGroupId)) return _i18n.T("noLessons");
        var groupId = settings.MyGroupId!;
        // Determine parity for date
        DateTime periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(DateTime.Now.Year, 9, 1);
        int wc = settings.WeekCount > 0 ? settings.WeekCount : 2;
        bool isOdd = ParityService.IsOddWeek(date, periodStart, wc, settings.ParityInvert);
        string parityStr = _i18n.FormatParity(isOdd);
        string dayName = _i18n.FormatDay(date);
        var lessons = _scheduleService.GetSchedule(date, groupId);
        if (lessons.Count == 0)
        {
            return $"{dayName}, {parityStr}: {_i18n.T("notifNoLessons")}";
        }
        var sb = new StringBuilder();
        sb.Append($"{dayName}, {parityStr}: ");
        int n = 1;
        foreach (var l in lessons.OrderBy(x => x.TimeStart))
        {
            string display = _overrideService.GetDisplayName(l.SubjectRaw, (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek);
            var hws = _homeworkService.GetForSubject(l.SubjectRaw);
            var burning = hws.FirstOrDefault(h => h.Status == "burning" || h.Status == "burning_urgent");
            string hwMark = burning != null ? $" {_i18n.T("notifBurning")}" : "";
            sb.Append($"{n++}. {display} {l.ClassroomRaw}{hwMark}; ");
        }
        return sb.ToString().TrimEnd(' ', ';');
    }
}
