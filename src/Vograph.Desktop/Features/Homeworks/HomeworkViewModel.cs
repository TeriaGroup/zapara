using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly HomeworkComposer _composer;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private int _version;
    private readonly HashSet<string> _expanded = new();

    public HomeworkViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _composer = new HomeworkComposer(app);
        _reload = () => _ = LoadAsync();
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
        shell.HomeworkChanged += _reload;
        app.Loc.LanguageChanged += _reload;
    }

    public override void Detach()
    {
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
        _shell.HomeworkChanged -= _reload;
        App.Loc.LanguageChanged -= _reload;
    }

    public override Task ActivateAsync() => LoadAsync();

    public string Title => T("navHomework");
    public ObservableCollection<HomeworkGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasGroup = true;

    public async Task LoadAsync()
    {
        var version = ++_version;
        var today = _clock().Date;
        var model = await RunAsync(() => _composer.Compose(today), "homework");
        if (model is null || version != _version) return;
        HasGroup = model.HasGroup;
        Groups.Clear();
        foreach (var g in model.Groups)
            Groups.Add(new HomeworkGroupViewModel(g, this, collapsed: g.Status == "done" && !_expanded.Contains(g.Status)));
        IsEmpty = model.HasGroup && model.Groups.Count == 0;
        Subtitle = $"{App.Loc.Plural(model.Open, "hwOpen1", "hwOpen2", "hwOpen5")} · {T("hwDoneCount", model.Done)}";
        OnPropertyChanged(nameof(Title));
    }

    internal void Toggled(HomeworkGroupViewModel g)
    {
        if (g.IsCollapsed) _expanded.Remove(g.Status); else _expanded.Add(g.Status);
    }

    /// <summary>Two steps: which subject, then the shared homework dialog with due dates counted from today.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Add()
    {
        var subjects = await RunAsync(() => _composer.Subjects(), "homework subjects");
        if (subjects is null || subjects.Count == 0) return;
        var pick = new SubjectPickerDialogViewModel(subjects);
        if (!await _shell.Dialogs.ShowAsync(pick) || pick.Selected is null) return;
        var subject = pick.Selected;
        var today = _clock().Date;
        var norm = ParityService.NormalizeSubject(subject.SubjectRaw);
        var dues = await RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(norm, today, n)).ToArray(), "homework");
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(subject.Display, nth => dues[Math.Clamp(nth, 1, 10) - 1]);
        if (!await _shell.Dialogs.ShowAsync(dlg)) return;
        if (await RunAsync(() => App.Homework.AddHomework(subject.SubjectRaw, dlg.Text.Trim(), dlg.Nth, createdAt: today), "homework add"))
            await ChangedAsync();
    }

    public async Task EditAsync(HomeworkRowViewModel row)
    {
        var existing = await RunAsync<Core.Models.Homework>(() => App.Homework.GetById(row.Entry.Homework.Id)!, "homework edit");
        if (existing is null) return;
        var dues = await RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(existing.SubjectRawNormalized, existing.CreatedAt, n)).ToArray(), "homework");
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(row.Subject, nth => dues[Math.Clamp(nth, 1, 10) - 1], existing.Text, existing.TargetNthOccurrence);
        if (!await _shell.Dialogs.ShowAsync(dlg)) return;
        if (await RunAsync(() => App.Homework.UpdateHomework(existing.Id, dlg.Text.Trim(), dlg.Nth), "homework edit"))
            await ChangedAsync();
    }

    public async Task ToggleDoneAsync(HomeworkRowViewModel row)
    {
        if (await RunAsync(() => App.Homework.MarkDone(row.Entry.Homework.Id, !row.IsDone), "homework done"))
            await ChangedAsync();
    }

    public async Task DeleteAsync(HomeworkRowViewModel row)
    {
        var confirm = new ConfirmDialogViewModel(T("hwDelete"), T("hwDeleteConfirm", row.Text), T("delete"), danger: true);
        if (!await _shell.Dialogs.ShowAsync(confirm)) return;
        if (await RunAsync(() => App.Homework.Delete(row.Entry.Homework.Id), "homework delete"))
            await ChangedAsync();
    }

    private async Task ChangedAsync()
    {
        await LoadAsync();
        _shell.RaiseHomeworkChanged(); // schedule cards + sidebar badge
    }
}

public sealed partial class HomeworkGroupViewModel : ObservableObject
{
    private readonly HomeworkViewModel _owner;

    public HomeworkGroupViewModel(HomeworkGroup group, HomeworkViewModel owner, bool collapsed)
    {
        _owner = owner;
        Status = group.Status;
        Title = group.Title;
        Items = group.Items.Select(e => new HomeworkRowViewModel(e, owner)).ToList();
        _isCollapsed = collapsed;
    }

    public string Status { get; }
    public string Title { get; }
    public IReadOnlyList<HomeworkRowViewModel> Items { get; }
    public int Count => Items.Count;
    public bool IsDone => Status == "done";
    public string CssClass => HomeworkLabels.StatusClass(Status);

    [ObservableProperty] private bool _isCollapsed;

    [RelayCommand]
    private void Toggle()
    {
        IsCollapsed = !IsCollapsed;
        _owner.Toggled(this);
    }
}

public sealed partial class HomeworkRowViewModel : ObservableObject
{
    private readonly HomeworkViewModel _owner;

    public HomeworkRowViewModel(HomeworkEntry entry, HomeworkViewModel owner)
    {
        Entry = entry;
        _owner = owner;
    }

    public HomeworkEntry Entry { get; }
    public string Subject => Entry.Subject;
    public string Text => Entry.Homework.Text;
    public string Label => Entry.Label;
    public string Status => Entry.Status;
    public bool IsDone => Entry.Status == "done";
    public bool IsApproaching => Entry.Status == "approaching";
    public bool IsBurning => Entry.Status == "burning";
    public bool IsUrgent => Entry.Status == "burning_urgent";
    public bool IsOverdue => Entry.Status == "overdue";
    public bool IsFar => Entry.Status == "far";
    public string DoneLabel => Loc.Current.T(IsDone ? "hwUndo" : "hwMarkDone");

    [RelayCommand] private Task ToggleDone() => _owner.ToggleDoneAsync(this);
    [RelayCommand] private Task Edit() => _owner.EditAsync(this);
    [RelayCommand] private Task Delete() => _owner.DeleteAsync(this);
}
