using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Controls;

namespace Vograph.Desktop.Features.Schedule;

public sealed record FriendMark(string GroupName, string MemberNames, int ColorIndex, DotFill Fill, string Tooltip);

public sealed record HomeworkItem(long Id, string Text, string Status, DateTime? Due, string Label, bool IsDone);

public sealed record LessonRow(
    Lesson Lesson,
    string TimeStart,
    string TimeEnd,
    string? NextDateText,
    string DisplayName,
    string? OriginalName,
    string? Note,
    string TypeLabel,
    string Teacher,
    string RoomText,
    string? BuildingTag,
    bool IsRemote,
    bool IsPast,
    bool IsNext,
    IReadOnlyList<FriendMark> Friends,
    IReadOnlyList<HomeworkItem> Homework,
    MapInfo? Map);

public sealed record DayModel(DateTime Date, int Offset, string Title, string Subtitle, IReadOnlyList<LessonRow> Rows, string? EmptyTitle, string? EmptyHint);
