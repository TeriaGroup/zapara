using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Homeworks;

/// <summary>Core's HomeworkService.ComputeStatus with an explicit "today" (Core reads DateTime.Today, which made
/// the stage-1 suite date-dependent). Thresholds are identical: overdue / due today / due tomorrow / one lesson
/// left or ≤ 3 days / far.</summary>
public static class HomeworkStatus
{
    public static string Compute(Homework hw, DateTime today, int lessonsBefore)
    {
        if (hw.Status == "done") return "done";
        if (hw.DueDateComputed is not { } due) return "pending";
        var days = (due.Date - today.Date).Days;
        if (days < 0) return "overdue";
        if (days == 0) return "burning_urgent";
        if (days == 1) return "burning";
        if (lessonsBefore == 1) return "approaching";
        if (lessonsBefore == 0 && days <= 3) return "approaching";
        return "far";
    }

    /// <summary>Section order (spec 5.7): Горит сегодня · Горит · Скоро · Далеко · Просрочено · Сдано.</summary>
    public static int GroupOrder(string status) => status switch
    {
        "burning_urgent" => 0, "burning" => 1, "approaching" => 2, "far" => 3, "overdue" => 4, "done" => 5, _ => 3
    };

    /// <summary>Sidebar badge: due today or tomorrow, plus overdue. Needs no lesson counting, so it is cheap.</summary>
    public static int BadgeCount(IEnumerable<Homework> all, DateTime today) =>
        all.Count(h => h.Status != "done" && h.DueDateComputed is { } due && (due.Date - today.Date).Days is < 0 or 0 or 1);
}
