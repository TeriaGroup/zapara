namespace Zapara.Web.Services;

public sealed record CatalogViewScope(string Owner, long Generation, string Group);
public sealed record WeekView(DateTime Anchor, int Parity = 0, int Day = 0, string Kind = "", string Subject = "");
public sealed record SummaryView(int Parity = 0, int Day = 0, string Kind = "", string Query = "");
public sealed record TeacherView(string Query = "", bool OnlyMine = false, string? SelectedId = null, int Parity = 0, int VisibleCount = 80);
public sealed class BrowserCatalogViewState
{
    private CatalogViewScope? active;
    private readonly Dictionary<(string, string), WeekView> weeks = [];
    private readonly Dictionary<(string, string), SummaryView> summaries = [];
    private readonly Dictionary<(string, string), TeacherView> teachers = [];
    public CatalogViewScope Activate(string owner, long generation, string group) => active = new(owner, generation, group);
    public WeekView Week(CatalogViewScope scope, DateTime today) { Require(scope); return weeks.GetValueOrDefault((scope.Owner, scope.Group)) ?? new(today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7))); }
    public SummaryView Summary(CatalogViewScope scope) { Require(scope); return summaries.GetValueOrDefault((scope.Owner, scope.Group)) ?? new(); }
    public TeacherView Teachers(CatalogViewScope scope) { Require(scope); return teachers.GetValueOrDefault((scope.Owner, scope.Group)) ?? new(); }
    public bool Save(CatalogViewScope scope, WeekView value) { if (scope != active) return false; weeks[(scope.Owner, scope.Group)] = value; return true; }
    public bool Save(CatalogViewScope scope, SummaryView value) { if (scope != active) return false; summaries[(scope.Owner, scope.Group)] = value; return true; }
    public bool Save(CatalogViewScope scope, TeacherView value) { if (scope != active) return false; teachers[(scope.Owner, scope.Group)] = value; return true; }
    private void Require(CatalogViewScope scope) { if (scope != active) throw new InvalidOperationException("Профиль изменился."); }
}

public static class BrowserCatalogNavigation
{
    public static string SummaryTarget(string section, string value)
    {
        var escaped = Uri.EscapeDataString(value);
        return section switch
        {
            "day" => "week?day=" + Enumerable.Range(1, 6).FirstOrDefault(day => Vograph.Core.Services.ParityService.DayNumberToTitle(day).Equals(value, StringComparison.OrdinalIgnoreCase)),
            "type" => "week?kind=" + Uri.EscapeDataString(TypeCode(value)),
            "subject" => "week?subject=" + escaped,
            "teacher" => "teachers?query=" + escaped,
            "room" => "maps?room=" + escaped,
            _ => "week"
        };
    }
    public static string TypeCode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "лекция" => "лек", "практика" => "пр", "лабораторная" => "лаб", "консультация" => "конс",
        "зачёт" => "зач", "экзамен" => "экз", "курсовая" => "курс", "—" or "" => "__none__", var other => other
    };
}
