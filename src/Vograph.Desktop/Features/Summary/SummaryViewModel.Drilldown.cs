using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Shell;

namespace Vograph.Desktop.Features.Summary;

public sealed partial class SummaryViewModel
{
    private IReadOnlyList<SummaryLessonEntry> rawEntries = [];
    [ObservableProperty] private string summaryMatchTitle = "";
    [ObservableProperty] private IReadOnlyList<SummaryLessonRow> summaryMatches = [];
    [ObservableProperty] private string summaryRoomFeedback = "";
    public bool HasSummaryMatches => SummaryMatches.Count > 0;
    partial void OnSummaryMatchesChanged(IReadOnlyList<SummaryLessonRow> value) => OnPropertyChanged(nameof(HasSummaryMatches));
    private void ShowMatches(string title, Func<SummaryLessonEntry, bool> predicate)
    {
        SummaryMatchTitle = title;
        SummaryMatches = rawEntries.Where(predicate).OrderBy(entry => entry.Date).ThenBy(entry => entry.TimeStart)
            .Select(entry => new SummaryLessonRow(entry, this)).ToArray();
    }
    [RelayCommand] private void OpenSummarySubject(CountItem? item)
    {
        if (item is null || !Subjects.Contains(item)) return;
        ShowMatches($"Пары предмета · {item.Name}", entry => entry.Subject.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
    }
    [RelayCommand] private void OpenSummaryType(CountItem? item)
    {
        if (item is null || !Types.Contains(item)) return;
        ShowMatches($"Пары типа · {item.Name}", entry => entry.TypeLabel.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
    }
    [RelayCommand] private void OpenSummaryTeacher(CountItem? item)
    {
        if (item is null || !Teachers.Contains(item)) return;
        ShowMatches($"Пары преподавателя · {item.Name}", entry => entry.TeacherRaw.Split(';').Any(name =>
            name.Trim().Equals(item.Name, StringComparison.OrdinalIgnoreCase)));
        var teacher = _shell.Section<Features.Teachers.TeachersViewModel>(SectionKey.Teachers);
        _ = teacher.OpenByNameAsync(item.Name);
        _shell.NavigateTo(SectionKey.Teachers);
    }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenSummaryRoom(CountItem? item)
    {
        if (item is null || !Rooms.Contains(item)) return;
        ShowMatches($"Пары в аудитории · {item.Name}", entry => entry.ClassroomRaw.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var map = await RunAsync(() => App.Maps.Resolve(item.Name), "summary room map");
        if (!operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId) return;
        if (map is { HasMap: true }) _shell.ShowMap(map);
        else SummaryRoomFeedback = "Для этой аудитории нет координаты на сохранённой карте. Пары показаны ниже.";
    }
    [RelayCommand] private void HideSummaryMatches() { SummaryMatches = []; SummaryMatchTitle = ""; SummaryRoomFeedback = ""; }
    internal void OpenSummaryLesson(SummaryLessonEntry entry)
    {
        if (!rawEntries.Contains(entry)) return;
        _shell.OpenScheduleAt(entry.Date, entry.SubjectRaw, entry.TimeStart);
    }
    private void ResetSummaryMatches()
    { SummaryMatches = []; SummaryMatchTitle = ""; SummaryRoomFeedback = ""; }
}

public sealed partial class SummaryLessonRow(SummaryLessonEntry entry, SummaryViewModel owner) : ObservableObject
{
    public SummaryLessonEntry Entry { get; } = entry;
    public string Label => $"{Entry.Date:dd.MM.yyyy} · {Entry.TimeStart}–{Entry.TimeEnd} · {Entry.Subject} · " +
        $"{Entry.TypeLabel} · {Entry.TeacherRaw} · {Entry.ClassroomRaw}";
    [RelayCommand] private void Open() => owner.OpenSummaryLesson(Entry);
}
