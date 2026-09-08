using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Vograph.Core.Models;

namespace Zapara.Server.Timetable;

// This projection does not validate XML. Only the future input validator establishes validity.
public sealed class ValidatedSnapshot
{
    public ValidatedSnapshot(PeriodDto period, IEnumerable<GroupDto> groups,
        IEnumerable<SnapshotLesson> lessons, SourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(source);
        Period = period with { };
        Groups = groups.Select(group => group with { }).ToImmutableArray();
        Lessons = lessons.Select(lesson => lesson with { Value = lesson.Value with { } }).ToImmutableArray();
        Source = source;
    }

    public PeriodDto Period { get; }
    public ImmutableArray<GroupDto> Groups { get; }
    public ImmutableArray<SnapshotLesson> Lessons { get; }
    [JsonIgnore] public SourceDocument Source { get; }
    [JsonIgnore] public SnapshotPayload Payload => new(Period, Groups, Lessons);
}

public static class SnapshotMapper
{
    public static ValidatedSnapshot FromParsed(IEnumerable<Group> groups, IEnumerable<Lesson> lessons,
        DateTime periodStart, int weekCount, string title, SourceDocument source)
    {
        var projected = lessons.Select(lesson => new SnapshotLesson(lesson.GroupId,
            new LessonDto(lesson.DayOfWeek, lesson.Parity, lesson.Index,
                lesson.TimeStart, lesson.TimeEnd, lesson.SubjectRaw, lesson.SubjectNormalized,
                lesson.TypeRaw, lesson.TeacherRaw, lesson.ClassroomRaw, lesson.RoomRaw, lesson.BuildingRaw)))
            .ToImmutableArray();
        var counts = projected.GroupBy(lesson => lesson.GroupId).ToDictionary(group => group.Key, group => group.Count());
        return new ValidatedSnapshot(new PeriodDto(DateOnly.FromDateTime(periodStart), weekCount, title, "Europe/Moscow"),
            groups.Select(group => new GroupDto(group.Id, group.Name, counts.GetValueOrDefault(group.Id))), projected, source);
    }
}
