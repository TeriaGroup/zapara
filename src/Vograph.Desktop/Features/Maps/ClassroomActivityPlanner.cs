using Vograph.Core.Campus;
using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Maps;

public sealed record ClassroomActivityEntry(DateTime Date, string GroupId, string GroupName,
    string TimeStart, string TimeEnd, string SubjectRaw, string TypeRaw, string TeacherRaw, string ClassroomRaw)
{
    public string Label => $"{TimeStart}–{TimeEnd} · {GroupName} · {SubjectRaw} · {TeacherRaw}";
    public bool SameLesson(Lesson lesson) => lesson.GroupId == GroupId && lesson.TimeStart == TimeStart &&
        lesson.TimeEnd == TimeEnd && lesson.SubjectRaw == SubjectRaw && lesson.TypeRaw == TypeRaw &&
        lesson.TeacherRaw == TeacherRaw && lesson.ClassroomRaw == ClassroomRaw;
}

public sealed record ClassroomActivityResult(IReadOnlyList<ClassroomActivityEntry> Items,
    int KnownGroups, int TotalGroups, int UnmatchedLessons);

public static class ClassroomActivityPlanner
{
    public static ClassroomActivityResult Create(CampusGraph graph, string roomId, DateTime date,
        IReadOnlyList<Group> groups, Func<Group, bool> hasCopy, Func<string, IReadOnlyList<Lesson>> schedule)
    {
        if (graph.Nodes.All(node => node.Id != roomId || node.Kind != "room"))
            return new([], 0, groups.Count, 0);
        var items = new List<ClassroomActivityEntry>(); var known = 0; var unmatched = 0;
        foreach (var group in groups)
        {
            if (!hasCopy(group)) continue;
            known++;
            foreach (var lesson in schedule(group.Id))
            {
                var room = CampusRouter.ResolveClassroom(graph, lesson.ClassroomRaw);
                if (room is null) { unmatched++; continue; }
                if (room.Id != roomId) continue;
                items.Add(new(date.Date, group.Id, group.Name, lesson.TimeStart, lesson.TimeEnd,
                    lesson.SubjectRaw, lesson.TypeRaw, lesson.TeacherRaw, lesson.ClassroomRaw));
            }
        }
        return new(items.OrderBy(item => item.TimeStart).ThenBy(item => item.GroupName).ToArray(),
            known, groups.Count, unmatched);
    }
}
