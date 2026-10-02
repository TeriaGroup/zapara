using Vograph.Core.Campus;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Schedule;

public static class ScheduleTransitionPlanner
{
    public static TransferAssessment Assess(CampusGraph? graph, string previousEnd, string nextStart,
        string previousClassroom, string nextClassroom)
    {
        int? seconds = null;
        if (previousClassroom.Length > 0 && previousClassroom.Equals(nextClassroom, StringComparison.OrdinalIgnoreCase))
            seconds = 0;
        else if (graph is not null)
        {
            var from = CampusRouter.ResolveClassroom(graph, previousClassroom);
            var destination = CampusRouter.ResolveClassroom(graph, nextClassroom);
            if (from is not null && destination is not null)
            {
                var route = CampusRouter.Find(graph, from.Id, destination.Id);
                if (route.Ok) seconds = route.Route!.Seconds;
            }
        }
        return StudyPlanning.AssessTransfer(previousEnd, nextStart, seconds);
    }

    public static IReadOnlyList<string> Warnings(CampusGraph? graph, IReadOnlyList<LessonRow> rows)
    {
        var ordered = rows.OrderBy(row => TimeSpan.TryParse(row.TimeStart, out var time) ? time : TimeSpan.MaxValue).ToArray();
        var ambiguous = new bool[ordered.Length];
        for (var left = 0; left < ordered.Length; left++)
        for (var right = left + 1; right < ordered.Length; right++)
            if (Overlaps(ordered[left], ordered[right])) ambiguous[left] = ambiguous[right] = true;
        var warnings = new List<string>();
        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1]; var next = ordered[index];
            if (ambiguous[index - 1] || ambiguous[index])
            {
                if (Overlaps(previous, next)) warnings.Add($"{previous.TimeEnd}–{next.TimeStart}: пары пересекаются по времени.");
                continue;
            }
            var result = Assess(graph, previous.TimeEnd, next.TimeStart,
                previous.Lesson.ClassroomRaw, next.Lesson.ClassroomRaw);
            if (result.Status == "overlap")
                warnings.Add($"{previous.TimeEnd}–{next.TimeStart}: пары пересекаются по времени.");
            else if (result.Status == "tight" && result.AvailableSeconds is { } available && result.RouteSeconds is { } route)
                warnings.Add($"{previous.TimeEnd}–{next.TimeStart}: перерыв {available / 60} мин, маршрут около {(int)Math.Ceiling(route / 60d)} мин — времени мало.");
        }
        return warnings;
    }

    private static bool Overlaps(LessonRow first, LessonRow second) =>
        TimeSpan.TryParse(first.TimeStart, out var startA) && TimeSpan.TryParse(first.TimeEnd, out var endA) &&
        TimeSpan.TryParse(second.TimeStart, out var startB) && TimeSpan.TryParse(second.TimeEnd, out var endB) &&
        startA < endB && startB < endA;
}
