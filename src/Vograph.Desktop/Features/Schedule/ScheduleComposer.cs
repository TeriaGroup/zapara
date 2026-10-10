using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Schedule;

/// <summary>Turns Core data into a ready-to-render day. Synchronous and DB-heavy — always call from ViewModelBase.RunAsync.</summary>
public sealed class ScheduleComposer
{
    private readonly AppServices _app;

    private readonly string? groupOverride;
    public ScheduleComposer(AppServices app,string? groupOverride=null){_app=app;this.groupOverride=groupOverride;}
    private string? GroupId=>groupOverride??_app.Settings.MyGroupId;
    private bool HasCopy(string groupId)
    {
        if(!_app.Api.Configured)return _app.Db.GetGroup(groupId) is not null;
        var metadata=new TimetableApiCache(_app.Db).Read(groupId);
        return metadata?.Source=="api" || metadata?.FetchedAt is not null || _app.Db.GetGroup(groupId)?.LastFetchedAt is not null || _app.Db.GetAllLessonsForGroup(groupId).Count>0;
    }

    public int InitialOffset(DateTime now)
    {
        var settings = _app.Settings;
        if (string.IsNullOrEmpty(settings.MyGroupId)) return 0;
        return SmartStart.InitialOffset(_app.Schedule.GetSchedule(now.Date, settings.MyGroupId), now.TimeOfDay, now.DayOfWeek);
    }

    public DayModel Compose(int offset, DateTime now, int dateCount = 7)
    {
        var day = ComposeDay(offset, now);
        var dates = DayPlanning.VisibleDates(DateOnly.FromDateTime(now), DateOnly.FromDateTime(day.Date), dateCount)
            .Select(date => new PlannerDate(date.ToDateTime(TimeOnly.MinValue), string.IsNullOrEmpty(GroupId) || !HasCopy(GroupId!) || DateTime.TryParse(_app.Settings.PeriodStart,out var start) && date.ToDateTime(TimeOnly.MinValue)<start.Date
                ? null : _app.Schedule.GetSchedule(date.ToDateTime(TimeOnly.MinValue), GroupId!).Count)).ToArray();
        var intervals = day.Rows.Select(row => new DayInterval(ParseTime(row.TimeStart), ParseTime(row.TimeEnd))).ToArray();
        var breaks = DayPlanning.FreeTime(intervals);
        var summary = day.IsUnavailable || day.Rows.Count == 0 ? "" : $"Занятия {intervals.Min(row => row.Start):hh\\:mm}–{intervals.Max(row => row.End):hh\\:mm}";
        var copy = new TimetableApiCache(_app.Db).Read(GroupId??"")?.FetchedAt ?? _app.Settings.LastFetchedAt;
        var source = DateTimeOffset.TryParse(copy,out var stamp) ? $"Копия {stamp.ToLocalTime():dd.MM.yyyy HH:mm}" : "Локальное расписание";
        if(!_app.AllowNetwork || _app.Api.LastFailure==Vograph.Core.Models.TimetableApiFailure.Transport)source="Нет сети · "+source;
        else if(_app.Api.LastError is not null)source="Обновление не удалось · "+source;
        return day with { Dates = dates, Breaks = breaks, Summary = summary, SourceSummary=source };
    }

    private DayModel ComposeDay(int offset, DateTime now)
    {
        var loc = _app.Loc;
        var date = now.Date.AddDays(offset);
        var settings = _app.Settings;
        var title = DayTitles.Title(offset, date, loc);

        if (string.IsNullOrEmpty(GroupId))
            return new DayModel(date, offset, title, "", Array.Empty<LessonRow>(), loc.T("noGroup"), loc.T("noGroupHint"), NeedsGroup: true);

        var groupId = GroupId!;
        if(DateTime.TryParse(settings.PeriodStart,out var knownStart) && date.Date<knownStart.Date)
            return new DayModel(date,offset,title,$"{date:dddd, dd.MM.yyyy}",[],"Дата вне известного учебного периода","Выберите дату начиная с "+knownStart.ToString("dd.MM.yyyy"),IsUnavailable:true);
        if (!HasCopy(groupId))
            return new DayModel(date, offset, title, "", Array.Empty<LessonRow>(),
                loc.T("bootstrapError"), loc.T("bootstrapHint"), IsUnavailable: true);

        var period = ParityCodes.Period(settings, date);
        var isOdd = ParityCodes.IsOdd(date, settings, period);
        var weekNumber = ParityService.GetWeekNumber(date, period.PeriodStart);

        var allLessons = _app.Db.GetAllLessonsForGroup(groupId);
        var choices = _app.Db.GetSubgroupChoices(groupId);
        var subgroups = SubgroupRules.Build(allLessons);
        var lessons = _app.Schedule.GetSchedule(date, groupId).OrderBy(l => ParseTime(l.TimeStart)).ToList();
        var subtitle = DayTitles.Subtitle(date, isOdd, weekNumber, lessons.Count, loc) + (date.Year != now.Year ? $" · {date.Year} год" : "");

        if (lessons.Count == 0)
        {
            var isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            string? hint = null;
            DateTime? nextStudyDate = null;
            {
                var (next, nextDate) = _app.Maps.GetNextLesson(groupId, date.AddDays(1));
                if (next is not null)
                { nextStudyDate = nextDate; hint = loc.T("nextLessonHint", LessonText.ShortDate(nextDate), next.TimeStart); /* G-3: «Следующая пара: пн, 12 окт., 10:50» */ }
            }
            return new DayModel(date, offset, title, subtitle, Array.Empty<LessonRow>(), loc.T(isSunday ? "noLessonsSunday" : "noLessonsDay"), hint, NextStudyDate: nextStudyDate);
        }

        var allHomework = _app.Homework.GetAll();
        var friends = _app.Db.GetFriends().Where(f => f.Enabled).Take(5).ToList();
        var friendSchedules = FriendMarks.PrepareDay(_app.Db, _app.Intersections, groupId, date, friends, settings);
        var isToday = offset == 0;
        var priority = DayPlanning.PriorityIndex(lessons.Select(x=>new DayInterval(ParseTime(x.TimeStart),ParseTime(x.TimeEnd))).ToArray(),DateOnly.FromDateTime(date),DateOnly.FromDateTime(now),now.TimeOfDay);
        var lessonIndex = 0;
        var rows = new List<LessonRow>(lessons.Count);

        foreach (var l in lessons)
        {
            var isPast = isToday && ParseTime(l.TimeEnd) <= now.TimeOfDay;
            var isNext = lessonIndex++ == priority;
            var isUpcoming = TimeSpan.TryParse(l.TimeStart, out var start) && start >= TimeSpan.Zero && start < TimeSpan.FromDays(1)
                && (date > now.Date || date == now.Date && start > now.TimeOfDay);

            // Core keys overrides/homework by the FULL Discipline ("лек ВЫСШ. МАТЕМАТ"); the type token is stripped for display only.
            var shownName = LessonText.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw);
            var shownOriginal = LessonText.StripType(l.SubjectRaw, l.TypeRaw);
            var note = _app.Overrides.GetNote(l.SubjectRaw, l.DayOfWeek);
            var map = _app.Maps.Resolve(l.ClassroomRaw);
            var (roomText, tag, remote) = LessonText.RoomParts(l, map, loc);
            var next = NextOccurrence.Find(allLessons, choices, settings, l.SubjectRaw, date);
            var subject = ParityService.NormalizeSubject(l.SubjectRaw);
            var homework = allHomework.Where(h => ParityService.SameSubject(h.SubjectRawNormalized, subject))
                .Select(h => ToItem(h, settings, now.Date, loc, allLessons, choices))
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
                Teacher: string.IsNullOrWhiteSpace(l.TeacherRaw) ? "—" : LessonText.Teacher(l.TeacherRaw),
                RoomText: roomText,
                BuildingTag: tag,
                IsRemote: remote,
                IsPast: isPast,
                IsNext: isNext,
                Friends: FriendMarks.Compute(l, friendSchedules, settings, loc, isUpcoming),
                Homework: homework,
                Map: map,
                Subgroup: ToSubgroup(SubgroupRules.MarkOf(l, lessons, subgroups, choices)),
                HasConflict: lessons.Any(other => !ReferenceEquals(l,other) && ParseTime(l.TimeStart)<ParseTime(other.TimeEnd) && ParseTime(l.TimeEnd)>ParseTime(other.TimeStart)),
                IsUpcoming: isUpcoming));
        }
        return new DayModel(date, offset, title, subtitle, rows, null, null);
    }

    private static SubgroupChoice? ToSubgroup(SubgroupRules.Mark? mark) =>
        mark is null ? null : new SubgroupChoice(mark.StreamId, mark.Options, mark.ChosenId, mark.ShowChooser);

    private static TimeSpan ParseTime(string s) => TimeSpan.TryParse(s, out var t) ? t : TimeSpan.Zero;

    /// <summary>The card's status comes from the composer's own clock, not from Core's persisted Status
    /// (Core recomputes it against DateTime.Today, which made the cards drift with the wall clock).</summary>
    private HomeworkItem ToItem(Homework h, Settings settings, DateTime today, Loc loc,
        IReadOnlyList<Lesson> allLessons, IReadOnlyDictionary<string, string> choices)
    {
        var until = h.Status != "done" && h.DueDateComputed is { } due && due.Date > today.Date
            ? HomeworkLabels.LessonsUntil(allLessons, choices, settings, h.SubjectRawNormalized, today, due)
            : 0;
        var status = HomeworkStatus.Compute(h, today, until);
        if (status == "pending") status = "far";
        return new HomeworkItem(h.Id, h.Text, status, h.DueDateComputed, HomeworkLabels.Label(status, h.DueDateComputed, until, loc), status == "done");
    }

    private static int Order(string status) => status switch
    {
        "burning_urgent" => 0, "burning" => 1, "overdue" => 2, "approaching" => 3, "done" => 5, _ => 4
    };
}
