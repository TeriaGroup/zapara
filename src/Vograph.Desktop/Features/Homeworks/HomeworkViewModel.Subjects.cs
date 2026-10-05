using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Domain;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel
{
    private string? overviewSubjectKey;
    public IReadOnlyList<HomeworkSubjectOverviewRow> SubjectOverview => _model?.Groups.SelectMany(group => group.Items)
        .GroupBy(entry => entry.Homework.SubjectRawNormalized, StringComparer.OrdinalIgnoreCase)
        .Select(group => new HomeworkSubjectOverviewRow(group.Key, group.First().Subject,
            group.Count(entry => entry.Homework.Status != "done"), group.Count(entry => entry.Homework.Status == "done"),
            group.Count(entry => entry.Homework.Status != "done" && entry.Due is { } due && due.Date < _clock().Date),
            group.Count(entry => entry.Due is null),
            group.Where(entry => entry.Homework.Status != "done").Select(entry => entry.Due).OfType<DateTime>()
                .OrderBy(date => date).Select(date => (DateTime?)date).FirstOrDefault(), OpenSubjectOverview))
        .OrderBy(row => row.Subject, StringComparer.CurrentCultureIgnoreCase).ToArray() ?? [];
    public bool HasSubjectOverview => SubjectOverview.Count > 0;

    private void OpenSubjectOverview(HomeworkSubjectOverviewRow row)
    {
        if (!SubjectOverview.Any(current => current.SubjectKey == row.SubjectKey)) return;
        SearchQuery = ""; StatusFilter = 2; DeadlineFilter = 0; OriginFilter = 1; FilesOnly = false;
        overviewSubjectKey = row.SubjectKey;
        SubjectFilter = row.Subject;
        ApplyFilters();
    }

    private void RefreshSubjectOverview()
    { OnPropertyChanged(nameof(SubjectOverview)); OnPropertyChanged(nameof(HasSubjectOverview)); }
}

public sealed class HomeworkSubjectOverviewRow
{
    public HomeworkSubjectOverviewRow(string subjectKey, string subject, int active, int done, int overdue,
        int noDate, DateTime? nearestDue, Action<HomeworkSubjectOverviewRow> open)
    {
        SubjectKey = subjectKey; Subject = subject; Active = active; Done = done; Overdue = overdue; NoDate = noDate;
        NearestDue = nearestDue; OpenCommand = new RelayCommand(() => open(this));
    }
    public string SubjectKey { get; }
    public string Subject { get; }
    public int Active { get; }
    public int Done { get; }
    public int Overdue { get; }
    public int NoDate { get; }
    public DateTime? NearestDue { get; }
    public string Summary => $"Активных: {Active} · готово: {Done} · просрочено: {Overdue} · без срока: {NoDate}" +
        (NearestDue is { } due ? $" · ближайший срок: {due:dd.MM.yyyy}" : "");
    public IRelayCommand OpenCommand { get; }
}
