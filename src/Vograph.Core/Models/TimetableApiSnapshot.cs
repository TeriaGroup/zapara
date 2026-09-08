using System.Collections.Immutable;

namespace Vograph.Core.Models;

public sealed record TimetableApiPeriod(DateOnly Start, int WeekCount, string Title, string TimeZone);
public sealed record TimetableApiMeta(Guid SnapshotId, DateTimeOffset FetchedAt, DateTimeOffset PublishedAt,
    DateTimeOffset? SourceModifiedAt, string SourceKind, string? SourceUrl, string SourceSha256, bool Stale);
public sealed record TimetableApiRefresh(Guid? LastAttemptId, string? LastAttemptStatus,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? LastFailureCode, bool Abandoned);
public sealed record TimetableApiGroup(string Id, string Name, int LessonCount)
{
    // A fresh mutable consumer object, never an alias into the validated payload.
    public Group ToGroup() => new() { Id = Id, Name = Name };
}

public sealed record TimetableApiLesson(int DayOfWeek, int Parity, int Index, string TimeStart,
    string TimeEnd, string SubjectRaw, string SubjectNormalized, string? TypeRaw, string? TeacherRaw,
    string? ClassroomRaw, string? RoomRaw, string? BuildingRaw)
{
    public Lesson ToLesson(string groupId) => new()
    {
        GroupId = groupId, DayOfWeek = DayOfWeek, Parity = Parity, Index = Index,
        TimeStart = TimeStart, TimeEnd = TimeEnd, SubjectRaw = SubjectRaw,
        SubjectNormalized = SubjectNormalized, TypeRaw = TypeRaw ?? "", TeacherRaw = TeacherRaw ?? "",
        ClassroomRaw = ClassroomRaw ?? "", RoomRaw = RoomRaw ?? "", BuildingRaw = BuildingRaw ?? ""
    };
}

public sealed record TimetableApiDownloadedGroup(TimetableApiGroup Group, TimetableApiMeta Meta,
    TimetableApiRefresh Refresh, ImmutableArray<TimetableApiLesson> Lessons);

/// <summary>Absent dictionary entry means unfetched, NOT an empty/current timetable.</summary>
public sealed record TimetableApiSnapshot(TimetableApiPeriod Period, TimetableApiMeta Meta,
    TimetableApiRefresh Refresh, ImmutableArray<TimetableApiGroup> Groups,
    ImmutableDictionary<string, TimetableApiDownloadedGroup> DownloadedGroups);

public enum TimetableApiFailure
{
    InvalidPayload, UnknownRequiredGroup, SnapshotUnavailable, ServerUnavailable, BodyTooLarge, Timeout, Transport
}

public sealed class TimetableApiException(TimetableApiFailure failure)
    : Exception("Не удалось получить проверенное расписание. Локальные данные сохранены.")
{
    public TimetableApiFailure Failure { get; } = failure;
}
