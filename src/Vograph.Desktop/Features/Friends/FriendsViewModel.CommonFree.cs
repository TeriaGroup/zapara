using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Friends;

public sealed partial class FriendsViewModel
{
    [ObservableProperty] private FriendItemViewModel? selectedComparisonFriend;
    [ObservableProperty] private DateTime? comparisonDate;
    [ObservableProperty] private IReadOnlyList<string> comparisonWindows = [];
    [ObservableProperty] private string comparisonStatus = "";
    private sealed record CommonWindowsResult(IReadOnlyList<FreeStudyInterval> Windows, string Status);
    partial void OnSelectedComparisonFriendChanged(FriendItemViewModel? value)
    { ComparisonWindows = []; ComparisonStatus = ""; }
    partial void OnComparisonDateChanged(DateTime? value)
    { ComparisonWindows = []; ComparisonStatus = ""; }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CompareFreeIntervals()
    {
        var friend = SelectedComparisonFriend;
        var date = ComparisonDate?.Date;
        if (friend is null || date is null || !Friends.Contains(friend))
        { ComparisonStatus = "Выберите группу друзей и дату."; ComparisonWindows = []; return; }
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        var friendName = friend.GroupName;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var result = await RunAsync(() =>
        {
            var settings = App.Db.GetSettings();
            var ownId = settings.MyGroupId ?? "";
            var other = App.Db.GetAllGroups().FirstOrDefault(group =>
                group.Name.Equals(friendName, StringComparison.OrdinalIgnoreCase) ||
                group.Id.Equals(friendName, StringComparison.OrdinalIgnoreCase));
            if (ownId.Length == 0 || other is null || other.Id == ownId)
                return new CommonWindowsResult([], "Для сравнения нужны две разные учебные группы.");
            var cache = new TimetableApiCache(App.Db);
            if (!cache.CanIntersect(ownId, other.Id))
                return new CommonWindowsResult([], "Нет совместимой сохранённой копии расписаний этих групп.");
            var own = App.Schedule.GetSchedule(date.Value, ownId);
            var theirs = App.Schedule.GetSchedule(date.Value, other.Id);
            if (own.Count == 0 || theirs.Count == 0)
                return new CommonWindowsResult([], "Для одной или обеих групп на выбранную дату нет занятий; свободное время не предполагается.");
            static bool Valid(IReadOnlyList<Vograph.Core.Models.Lesson> lessons) => lessons.All(lesson =>
                TimeSpan.TryParse(lesson.TimeStart, out var start) &&
                TimeSpan.TryParse(lesson.TimeEnd, out var end) && end > start);
            if (!Valid(own) || !Valid(theirs))
                return new CommonWindowsResult([], "В расписании неверное время пары; общий интервал не рассчитан.");
            var windows = StudyPlanning.CommonFreeIntervals(
                own.Select(lesson => new StudyInterval(lesson.TimeStart, lesson.TimeEnd)),
                theirs.Select(lesson => new StudyInterval(lesson.TimeStart, lesson.TimeEnd)));
            return new CommonWindowsResult(windows, windows.Count == 0 ? "На выбранную дату общих окон от 15 минут нет."
                : $"Общие свободные интервалы: {windows.Count}.");
        }, "common free intervals");
        if (result is null || !operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId ||
            SelectedComparisonFriend?.GroupName != friendName || ComparisonDate?.Date != date) return;
        ComparisonWindows = result.Windows.Select(window => $"{window.Start}–{window.End} · {window.Minutes} мин").ToArray();
        ComparisonStatus = result.Status;
    }
}
