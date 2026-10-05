using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Week;

public sealed partial class WeekViewModel
{
    [ObservableProperty] private DateTime? assessmentStartDate;
    [ObservableProperty] private IReadOnlyList<AssessmentChoice> assessments = [];
    [ObservableProperty] private string assessmentStatus = "";
    [ObservableProperty] private bool showAssessmentBoard;
    public bool HasAssessments => Assessments.Count > 0;
    partial void OnAssessmentsChanged(IReadOnlyList<AssessmentChoice> value) => OnPropertyChanged(nameof(HasAssessments));
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ShowAssessments()
    {
        var group = App.Settings.MyGroupId;
        if (string.IsNullOrWhiteSpace(group) || AssessmentStartDate is null)
        { AssessmentStatus = "Выберите свою группу и дату начала."; return; }
        var date = AssessmentStartDate.Value.Date;
        var scope = App.Profile.DatabasePath + ":" + group;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var result = await RunAsync(() =>
        {
            var settings = App.Db.GetSettings();
            var hasCopy = App.Db.GetAllLessonsForGroup(group).Count > 0 ||
                new TimetableApiCache(App.Db).Read(group) is { } cache &&
                (cache.FetchedAt is not null || cache.Source == "api");
            if (!hasCopy) return (AssessmentWindow?)null;
            return AssessmentPlanner.Create(group, date,
                DateTime.TryParse(settings.PeriodStart, out var period) ? period : null,
                day => App.Schedule.GetSchedule(day, group));
        }, "assessment plan");
        if (!operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId ||
            AssessmentStartDate?.Date != date) return;
        if (result is null)
        { Assessments = []; AssessmentStatus = "Нет сохранённой копии расписания группы для обзора оценок."; ShowAssessmentBoard = true; return; }
        Assessments = result.Items.Select(item => new AssessmentChoice(item, this)).ToArray();
        AssessmentStatus = $"Оценок в известных датах: {Assessments.Count}; дней до начала периода без данных: {result.UnknownDays}.";
        ShowAssessmentBoard = true;
    }
    [RelayCommand] private void HideAssessments()
    { ShowAssessmentBoard = false; Assessments = []; AssessmentStatus = ""; }
    internal async Task OpenAssessmentAsync(AssessmentEntry entry)
    {
        if (!Assessments.Any(row => row.Entry == entry)) return;
        var scope = App.Profile.DatabasePath + ":" + entry.GroupId;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var matches = await RunAsync(() => App.Schedule.GetSchedule(entry.Date, entry.GroupId)
            .Where(entry.SameLesson).Take(2).ToArray(), "assessment target");
        if (matches?.Length != 1 || !operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId)
        { if (operation.IsCurrent) App.Toasts.Info("Пара изменилась в сохранённом расписании."); return; }
        _shell.OpenScheduleAt(entry.Date, entry.SubjectRaw, entry.TimeStart, entry.TimeEnd,
            entry.TypeRaw, entry.TeacherRaw, entry.ClassroomRaw);
    }
}

public sealed partial class AssessmentChoice(AssessmentEntry entry, WeekViewModel owner) : ObservableObject
{
    public AssessmentEntry Entry { get; } = entry;
    public string Label => entry.Label;
    [RelayCommand] private Task Open() => owner.OpenAssessmentAsync(Entry);
}
