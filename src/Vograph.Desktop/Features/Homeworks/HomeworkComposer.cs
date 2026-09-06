using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Homeworks;

public sealed record HomeworkEntry(Homework Homework, string Subject, string SubjectRaw, string Status, DateTime? Due, string Label);
public sealed record HomeworkGroup(string Status, string Title, IReadOnlyList<HomeworkEntry> Items);
public sealed record HomeworkModel(bool HasGroup, int Open, int Done, IReadOnlyList<HomeworkGroup> Groups);
public sealed record SubjectOption(string SubjectRaw, string Display, string TypeLabel);

/// <summary>All homework grouped by status. DB-bound: call under RunAsync.</summary>
public sealed class HomeworkComposer
{
    private static readonly Dictionary<string, string> TitleKeys = new()
    {
        ["burning_urgent"] = "hwGroupUrgent", ["burning"] = "hwGroupBurning", ["approaching"] = "hwGroupApproaching",
        ["far"] = "hwGroupFar", ["overdue"] = "hwGroupOverdue", ["done"] = "hwGroupDone"
    };
    private readonly AppServices _app;

    public HomeworkComposer(AppServices app) => _app = app;

    public HomeworkModel Compose(DateTime today)
    {
        var settings = _app.Settings;
        if (string.IsNullOrEmpty(settings.MyGroupId)) return new HomeworkModel(false, 0, 0, Array.Empty<HomeworkGroup>());
        var loc = _app.Loc;
        var names = SubjectIndex(settings.MyGroupId);
        var entries = new List<HomeworkEntry>();
        foreach (var h in _app.Homework.GetAll())
        {
            var until = h.Status != "done" && h.DueDateComputed is { } due && due.Date > today.Date
                ? HomeworkLabels.LessonsUntil(_app.Db, settings, h.SubjectRawNormalized, today, due)
                : 0;
            var status = HomeworkStatus.Compute(h, today, until);
            var label = HomeworkLabels.Label(status == "pending" ? "far" : status, h.DueDateComputed, until, loc);
            var (display, raw) = names.TryGetValue(h.SubjectRawNormalized, out var n) ? n : (h.SubjectRawNormalized, h.SubjectRawNormalized);
            entries.Add(new HomeworkEntry(h, display, raw, status == "pending" ? "far" : status, h.DueDateComputed, label));
        }
        var groups = entries
            .GroupBy(e => e.Status)
            .OrderBy(g => HomeworkStatus.GroupOrder(g.Key))
            .Select(g => new HomeworkGroup(g.Key, loc.T(TitleKeys[g.Key]),
                g.OrderBy(e => e.Due ?? DateTime.MaxValue).ThenBy(e => e.Homework.CreatedAt).ToList()))
            .ToList();
        var done = entries.Count(e => e.Status == "done");
        return new HomeworkModel(true, entries.Count - done, done, groups);
    }

    /// <summary>Distinct subjects of my group for the «＋ Добавить» picker (full SubjectRaw is what Core is called with).</summary>
    public List<SubjectOption> Subjects()
    {
        var settings = _app.Settings;
        if (string.IsNullOrEmpty(settings.MyGroupId)) return new List<SubjectOption>();
        var loc = _app.Loc;
        return _app.Db.GetAllLessonsForGroup(settings.MyGroupId)
            .GroupBy(l => l.SubjectRaw, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(l => new SubjectOption(l.SubjectRaw, ScheduleComposer.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw), DayTitles.TypeLabel(l.TypeRaw, loc)))
            .OrderBy(s => s.Display, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true))
            .ToList();
    }

    /// <summary>normalized subject → (display name, full SubjectRaw); homework only stores the normalized key.</summary>
    private Dictionary<string, (string Display, string Raw)> SubjectIndex(string groupId)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in _app.Db.GetAllLessonsForGroup(groupId))
        {
            var key = ParityService.NormalizeSubject(l.SubjectRaw);
            if (map.ContainsKey(key)) continue;
            map[key] = (ScheduleComposer.StripType(_app.Overrides.GetDisplayName(l.SubjectRaw, l.DayOfWeek), l.TypeRaw), l.SubjectRaw);
        }
        return map;
    }
}
