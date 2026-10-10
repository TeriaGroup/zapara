using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Controls;

namespace Vograph.Desktop.Features.Schedule;

public sealed record FriendMark(string GroupName, string MemberNames, int ColorIndex, DotFill Fill, string Tooltip,
    bool? HasLesson = null, bool ShowLessonStatus = false);

public sealed record HomeworkItem(long Id, string Text, string Status, DateTime? Due, string Label, bool IsDone);

public sealed record SubgroupChoice(string StreamId, IReadOnlyList<SubgroupRules.Option> Options, string? ChosenId, bool ShowChooser);

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
    MapInfo? Map,
    SubgroupChoice? Subgroup = null, bool HasConflict = false, bool IsUpcoming = false);

public sealed record PlannerDate(DateTime Date, int? LessonCount);

public sealed record DayModel(DateTime Date, int Offset, string Title, string Subtitle, IReadOnlyList<LessonRow> Rows, string? EmptyTitle, string? EmptyHint, bool IsUnavailable = false,
    IReadOnlyList<PlannerDate>? Dates = null, IReadOnlyList<Zapara.Client.Domain.FreeTimeInterval>? Breaks = null, string? Summary = null, DateTime? NextStudyDate = null, string SourceSummary = "", bool NeedsGroup = false);
