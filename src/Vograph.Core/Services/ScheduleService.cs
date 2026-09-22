using Vograph.Core.Models;

namespace Vograph.Core.Services;

public class ScheduleService
{
    private readonly Database _db;

    public ScheduleService(Database db)
    {
        _db = db;
    }

    public List<Lesson> GetSchedule(DateTime date, string groupId, bool invertParity = false)
    {
        var settings = _db.GetSettings();
        DateTime periodStart;
        if (!string.IsNullOrEmpty(settings.PeriodStart) && DateTime.TryParse(settings.PeriodStart, out var ps))
            periodStart = ps;
        else
            periodStart = new DateTime(DateTime.Now.Year, 9, 1); // no stored period: week of 1 Sep is week 1

        int weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;
        int weekCode = ParityService.GetWeekCode(date, periodStart, weekCount);
        if (invertParity || settings.ParityInvert)
        {
            weekCode = weekCode == 1 ? 2 : 1;
        }

        int dow = (int)date.DayOfWeek;
        if (dow == 0) dow = 7; // Sunday
        if (dow == 7) return new List<Lesson>(); // No lessons Sunday

        var day = _db.GetLessons(groupId, dow, weekCode);
        if (string.IsNullOrEmpty(groupId)) return day;
        var index = SubgroupRules.Build(_db.GetAllLessonsForGroup(groupId));
        var choices = _db.GetSubgroupChoices(groupId);
        return day.Where(lesson => SubgroupRules.Keep(lesson, index, choices)).ToList();
    }

    public List<Lesson> GetScheduleForDayAndParity(string groupId, int dayOfWeek, int parity)
    {
        return _db.GetLessons(groupId, dayOfWeek, parity);
    }

    public List<Lesson> GetWeekView(string groupId, int parity, bool invert = false)
    {
        var settings = _db.GetSettings();
        if (invert || settings.ParityInvert)
            parity = parity == 1 ? 2 : 1;

        var result = new List<Lesson>();
        for (int d = 1; d <= 6; d++)
        {
            result.AddRange(_db.GetLessons(groupId, d, parity));
        }
        return result;
    }
}
