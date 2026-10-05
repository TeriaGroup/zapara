using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Maps;

public sealed partial class MapsViewModel
{
    private string? activityRoomId;
    [ObservableProperty] private DateTime? activityDate;
    [ObservableProperty] private IReadOnlyList<ClassroomActivityChoice> activityRows = [];
    [ObservableProperty] private string activityStatus = "";
    [ObservableProperty] private string activityRoomLabel = "";
    public bool ShowActivity => activityRoomId is not null;
    public bool HasActivityRows => ActivityRows.Count > 0;
    partial void OnActivityRowsChanged(IReadOnlyList<ClassroomActivityChoice> value) => OnPropertyChanged(nameof(HasActivityRows));
    partial void OnActivityDateChanged(DateTime? value)
    { if (activityRoomId is not null) { ActivityRows = []; ActivityStatus = "Дата изменилась. Повторите проверку сохранённых групп."; } }
    private void ClearRoomActivity()
    { activityRoomId = null; ActivityRows = []; ActivityStatus = ""; ActivityRoomLabel = ""; OnPropertyChanged(nameof(ShowActivity)); }
    [RelayCommand] private void ChooseRoomActivity(MapPlaceChoice? place)
    {
        if (place is null || _graph.Nodes.All(node => node.Id != place.Id || node.Kind != "room")) return;
        activityRoomId = place.Id; ActivityRoomLabel = place.Label;
        ActivityDate ??= _clock().Date; ActivityRows = [];
        ActivityStatus = "Показаны занятия только групп, чьи копии расписания сохранены на этом устройстве.";
        OnPropertyChanged(nameof(ShowActivity));
    }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadRoomActivity()
    {
        if (activityRoomId is not { } room || ActivityDate is not { } chosen) return;
        var date = chosen.Date; var group = App.Settings.MyGroupId;
        var scope = App.Profile.DatabasePath + ":" + group;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var result = await RunAsync(() =>
        {
            var settings = App.Db.GetSettings();
            var groups = App.Db.GetAllGroups();
            var cache = new TimetableApiCache(App.Db);
            var period = DateTime.TryParse(settings.PeriodStart, out var start) ? start.Date : DateTime.MinValue;
            return ClassroomActivityPlanner.Create(_graph, room, date, groups,
                candidate => date >= period && (candidate.LastFetchedAt is not null ||
                    App.Db.GetAllLessonsForGroup(candidate.Id).Count > 0 ||
                    cache.Read(candidate.Id)?.FetchedAt is not null),
                candidateId => App.Schedule.GetSchedule(date, candidateId));
        }, "cached classroom activity");
        if (result is null || !operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId ||
            activityRoomId != room || ActivityDate?.Date != date) return;
        ActivityRows = result.Items.Select(item => new ClassroomActivityChoice(item, this)).ToArray();
        ActivityStatus = $"Проверено групп: {result.KnownGroups} из {result.TotalGroups}; " +
            $"не сопоставлено с графом пар: {result.UnmatchedLessons}. " +
            (result.KnownGroups == 0 ? "Нет сохранённых копий для этой даты." :
                result.Items.Count == 0 ? "Среди проверенных копий занятий в аудитории не найдено." :
                    $"Найдено занятий: {result.Items.Count}.");
    }
    internal async Task OpenRoomActivityAsync(ClassroomActivityEntry entry)
    {
        if (!ActivityRows.Any(row => row.Entry == entry) || activityRoomId is not { } room ||
            ActivityDate?.Date != entry.Date.Date) return;
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var valid = await RunAsync(() => App.Schedule.GetSchedule(entry.Date, entry.GroupId).Any(entry.SameLesson),
            "classroom activity target");
        if (!valid || !operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId ||
            activityRoomId != room) { if (operation.IsCurrent) App.Toasts.Info("Занятие изменилось в сохранённой копии."); return; }
        if (entry.GroupId == App.Settings.MyGroupId)
            _shell.OpenScheduleAt(entry.Date, entry.SubjectRaw, entry.TimeStart, entry.TimeEnd,
                entry.TypeRaw, entry.TeacherRaw, entry.ClassroomRaw);
        else
        {
            ActivityStatus = $"{entry.Label}. Это занятие другой группы; ваша выбранная группа не изменена.";
            var node = _graph.Nodes.FirstOrDefault(item => item.Id == room);
            if (node is not null) await SelectPlace(new MapPlaceChoice(node.Id,
                $"{node.Room} · {node.Building}, {node.Floor} этаж", node.Building, node.Floor, node.Room ?? ""));
        }
    }
}

public sealed partial class ClassroomActivityChoice(ClassroomActivityEntry entry, MapsViewModel owner) : ObservableObject
{
    public ClassroomActivityEntry Entry { get; } = entry;
    public string Label => entry.Label;
    [RelayCommand] private Task Open() => owner.OpenRoomActivityAsync(Entry);
}
