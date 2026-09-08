using System.Collections.Immutable;

namespace Zapara.Server.Timetable;

public sealed record PeriodDto(DateOnly Start, int WeekCount, string Title, string TimeZone);
public sealed record GroupDto(string Id, string Name, int LessonCount);
public sealed record LessonDto(
    int DayOfWeek, int Parity, int Index,
    string TimeStart, string TimeEnd, string SubjectRaw, string SubjectNormalized,
    string? TypeRaw, string? TeacherRaw, string? ClassroomRaw, string? RoomRaw, string? BuildingRaw);
public sealed record SnapshotMetaDto(
    Guid SnapshotId, DateTimeOffset FetchedAt, DateTimeOffset PublishedAt,
    DateTimeOffset? SourceModifiedAt, string SourceKind, string? SourceUrl, string SourceSha256, bool Stale);
public sealed record RefreshDto(
    Guid? LastAttemptId, string? LastAttemptStatus,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt,
    string? LastFailureCode, bool Abandoned);
public sealed record StatusResponse(SnapshotMetaDto Meta, RefreshDto Refresh);
public sealed record GroupsResponse(PeriodDto Period, SnapshotMetaDto Meta, RefreshDto Refresh, ImmutableArray<GroupDto> Groups);
public sealed record TimetableResponse(PeriodDto Period, SnapshotMetaDto Meta, RefreshDto Refresh, GroupDto Group, ImmutableArray<LessonDto> Lessons);

public sealed record SnapshotLesson(string GroupId, LessonDto Value);
public sealed record SnapshotPayload(PeriodDto Period, ImmutableArray<GroupDto> Groups, ImmutableArray<SnapshotLesson> Lessons);
public sealed record SnapshotRead(SnapshotPayload Payload, SnapshotMetaDto Meta, RefreshDto Refresh);
public sealed record SnapshotSelection(Guid? CurrentId, SnapshotRead? Selected, RefreshDto Refresh);
