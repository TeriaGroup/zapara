using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

/// <summary>Turns Core data into a ready-to-render day. Synchronous and DB-heavy — always call from ViewModelBase.RunAsync.</summary>
public sealed class ScheduleComposer
{
    private readonly AppServices _app;

    public ScheduleComposer(AppServices app) => _app = app;

    public int InitialOffset(DateTime now)
    {
        var settings = _app.Settings;
        if (string.IsNullOrEmpty(settings.MyGroupId)) return 0;
        return SmartStart.InitialOffset(_app.Schedule.GetSchedule(now.Date, settings.MyGroupId), now.TimeOfDay);
    }

    public DayModel Compose(int offset, DateTime now)
    {
        var loc = _app.Loc;
        var date = now.Date.AddDays(offset);
        var settings = _app.Settings;
        var title = DayTitles.Title(offset, date, loc);

        if (string.IsNullOrEmpty(settings.MyGroupId))
            return new DayModel(date, offset, title, "", Array.Empty<LessonRow>(), loc.T("noGroup"), loc.T("noGroupHint"));

        var groupId = settings.MyGroupId;
        var periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(date.Year, 9, 1);
        var weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;
        var isOdd = ParityService.IsOddWeek(date, periodStart, weekCount, settings.ParityInvert);
        var weekNumber = ParityService.GetWeekNumber(date, periodStart);

        var lessons = _app.Schedule.GetSchedule(date, groupId).OrderBy(l => ParseTime(l.TimeStart)).ToList();
        var subtitle = DayTitles.Subtitle(date, isOdd, weekNumber, lessons.Count, loc);

        if (lessons.Count == 0)
        {
            var isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            string? hint = null;
            if (!isSunday)
            {
                var (next, nextDate) = _app.Maps.GetNextLesson(groupId, date.AddDays(1));
                if (next is not null)
                    hint = loc.T("nextLessonHint", loc.I18n.FormatDayFull(nextDate).ToLowerInvariant(), next.TimeStart);
            }
            return new DayModel(date, offset, title, subtitle, Array.Empty<LessonRow>(), loc.T(isSunday ? "noLessonsSunday" : "noLessonsDay"), hint);
        }

        var friends = _app.Db.GetFriends().Where(f => f.Enabled).Take(5).ToList();
        var isToday = offset == 0;
        var nextAssigned = false;
        var rows = new List<LessonRow>(lessons.Count);

        foreach (var l in lessons)
        {
            var isPast = isToday && ParseTime(l.TimeEnd) <= now.TimeOfDay;
            var isNext = isToday && !isPast && !nextAssigned;
            if (isNext) nextAssigned = true;

            // Core keys overrides/homework by the FULL Discipline ("лек ВЫСШ. МАТЕМАТ"); the type token is stripped for display only.
            var shownName = StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw);
            var shownOriginal = StripType(l.SubjectRaw, l.TypeRaw);
            var note = _app.Overrides.GetNote(l.SubjectRaw, l.DayOfWeek);
            var map = _app.Maps.Resolve(l.ClassroomRaw);
            var (roomText, tag, remote) = RoomParts(l, map, loc);
            var next = NextOccurrence.Find(_app.Db, settings, l.SubjectRaw, date);
            var homework = _app.Homework.GetForSubject(l.SubjectRaw)
                .Select(h => ToItem(h, settings, now.Date, loc))
                .OrderBy(h => Order(h.Status))
                .ToList();

            rows.Add(new LessonRow(
                Lesson: l,
                TimeStart: l.TimeStart,
                TimeEnd: l.TimeEnd,
                NextDateText: next is null ? null : loc.T("nextShort", DayTitles.ShortDate(next.Value, loc)),
                DisplayName: shownName,
                OriginalName: shownName == shownOriginal ? null : shownOriginal,
                Note: string.IsNullOrWhiteSpace(note) ? null : note,
                TypeLabel: DayTitles.TypeLabel(l.TypeRaw, loc),
                Teacher: string.IsNullOrWhiteSpace(l.TeacherRaw) ? "—" : l.TeacherRaw,
                RoomText: roomText,
                BuildingTag: tag,
                IsRemote: remote,
                IsPast: isPast,
                IsNext: isNext,
                Friends: FriendMarks.Compute(_app, l, date, friends, settings, loc),
                Homework: homework,
                Map: map));
        }
        return new DayModel(date, offset, title, subtitle, rows, null, null);
    }

    private static TimeSpan ParseTime(string s) => TimeSpan.TryParse(s, out var t) ? t : TimeSpan.Zero;

    /// <summary>"пр ОСН РОС ГОС" → "ОСН РОС ГОС" when the name starts with the lesson's own type token. Display only — never use for Core keys.</summary>
    public static string StripType(string name, string typeRaw)
    {
        var t = typeRaw.Trim();
        return t.Length > 0 && name.Length > t.Length + 1 && name.StartsWith(t + " ", StringComparison.OrdinalIgnoreCase)
            ? name[(t.Length + 1)..].Trim()
            : name;
    }

    public static (string Room, string? Tag, bool Remote) RoomParts(Lesson l, MapInfo? map, Loc loc)
    {
        if (map is null) return (string.IsNullOrWhiteSpace(l.RoomRaw) ? "—" : l.RoomRaw.Replace("*", "").Trim(), null, false);
        if (map.IsRemote) return (loc.T("remote"), null, true);
        if (map.Building == "ВЦ") return ($"ВЦ {map.RoomRaw}", "ГК", false);
        return (map.RoomRaw, map.Building, false);
    }

    private HomeworkItem ToItem(Homework h, Settings settings, DateTime today, Loc loc)
    {
        var status = h.Status == "done" ? "done" : _app.Homework.ComputeStatus(h);
        var until = h.DueDateComputed is { } due && status is not ("done" or "overdue")
            ? HomeworkLabels.LessonsUntil(_app.Db, settings, h.SubjectRawNormalized, today, due)
            : 0;
        return new HomeworkItem(h.Id, h.Text, status, h.DueDateComputed, HomeworkLabels.Label(status, h.DueDateComputed, until, loc), status == "done");
    }

    private static int Order(string status) => status switch
    {
        "burning_urgent" => 0, "burning" => 1, "overdue" => 2, "approaching" => 3, "done" => 5, _ => 4
    };
}
