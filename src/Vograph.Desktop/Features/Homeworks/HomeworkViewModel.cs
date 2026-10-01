using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
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
    private bool _raising;
    private readonly HashSet<string> _expanded = new();
    private HomeworkModel? _model;
    private string? _browseScope;
    private HomeworkCompletionUndo? _completionUndo;
    private readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public HomeworkViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _composer = new HomeworkComposer(app);
        _undoTimer.Tick += OnUndoTimer;
        _reload = () => { if (!_raising) _ = LoadAsync(); };
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
        shell.HomeworkChanged += _reload;
        app.Loc.LanguageChanged += _reload;
    }

    public override void Detach()
    {
        ClearCompletionUndo();
        _undoTimer.Tick -= OnUndoTimer;
        sharedRequestSerial++; sharedMutationSerial++; SharedTasks.Clear();
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
        _shell.HomeworkChanged -= _reload;
        App.Loc.LanguageChanged -= _reload;
    }

    public override Task ActivateAsync() => LoadAsync();

    public string Title => T("navHomework");
    private string CurrentBrowseScope() => App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
    private void EnsureBrowseScope()
    {
        var scope = CurrentBrowseScope();
        if (_browseScope is null) { _browseScope = scope; return; }
        if (_browseScope == scope) return;
        _browseScope = scope;
        _model = null; Groups.Clear(); _expanded.Clear(); IsLoaded = false; LoadFailed = false;
        ClearCompletionUndo();
        SubjectFilter = ""; SearchQuery = ""; StatusFilter = 0;
        NotifySharedTasks();
    }
    public bool HasCompletionUndo => _completionUndo is { } undo && undo.Scope == CurrentBrowseScope() && DateTimeOffset.UtcNow < undo.ExpiresAt;
    public string CompletionFeedback => _completionUndo?.BeforeDone == true ? "Отметка снята" : "Отмечено готово";
    private void OnUndoTimer(object? sender, EventArgs e) => ClearCompletionUndo();
    private void ClearCompletionUndo()
    {
        _undoTimer.Stop(); _completionUndo = null;
        OnPropertyChanged(nameof(HasCompletionUndo)); OnPropertyChanged(nameof(CompletionFeedback));
    }
    private void SetCompletionUndo(HomeworkCompletionUndo undo)
    {
        _undoTimer.Stop(); _completionUndo = undo;
        var remaining = undo.ExpiresAt - DateTimeOffset.UtcNow;
        _undoTimer.Interval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
        _undoTimer.Start();
        OnPropertyChanged(nameof(HasCompletionUndo)); OnPropertyChanged(nameof(CompletionFeedback));
    }
    [ObservableProperty] private string subjectFilter = "";
    public bool HasSubjectFilter => SubjectFilter.Length>0;
    partial void OnSubjectFilterChanged(string value){OnPropertyChanged(nameof(HasSubjectFilter));ApplyFilters();}
    [ObservableProperty] private string searchQuery = "";
    partial void OnSearchQueryChanged(string value) => ApplyFilters();
    [ObservableProperty] private int statusFilter;
    public IReadOnlyList<string> StatusFilters { get; } = ["Активные", "Готово у меня", "Все"];
    partial void OnStatusFilterChanged(int value) => ApplyFilters();
    [RelayCommand] private void ClearSubjectFilter() => SubjectFilter = "";
    [RelayCommand] private void ClearBrowseFilters() { SubjectFilter = ""; SearchQuery = ""; StatusFilter = 2; }
    public bool HasBrowseFilters => HasSubjectFilter || SearchQuery.Trim().Length > 0 || StatusFilter != 2;
    public bool ShowBrowseEmpty => IsLoaded && HasGroup && !LoadFailed && !SharedLoading && (!ShowSharedTasks || SharedLoaded) && Groups.Count == 0 && VisibleSharedTasks.Count == 0;
    public string BrowseEmptyTitle => TotalBrowseCount > 0 ? "По выбранным фильтрам заданий нет" : "Заданий пока нет";
    public string BrowseEmptyHint => TotalBrowseCount > 0 ? "Измените поиск или выберите другой статус." : "Добавьте личное задание или обновите задания группы.";
    private int TotalBrowseCount => (_model?.Open ?? 0) + (_model?.Done ?? 0) + (ShowSharedTasks && SharedLoaded ? SharedTasks.Count : 0);
    public string BrowseSummary
    {
        get
        {
            var shown = Groups.Sum(group => group.Items.Count) + VisibleSharedTasks.Count;
            var total = TotalBrowseCount;
            return ShowSharedTasks && !SharedLoaded
                ? SharedFeedback.Length > 0 ? $"Показано личных: {shown} · задания группы недоступны" : $"Показано личных: {shown} · загружаем задания группы…"
                : $"Показано: {shown} из {total}" + (LoadFailed ? " · последняя доступная копия" : "");
        }
    }
    public ObservableCollection<HomeworkGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool loadFailed;
    [ObservableProperty] private bool _hasGroup; // false until the first load: ShowNoGroup guards the empty-state flash (T8 #8)

    /// <summary>The «Группа не выбрана» state, only once the first load has said so.</summary>
    public bool ShowNoGroup => IsLoaded && !HasGroup;
    [RelayCommand] private Task RetryLoad() => LoadAsync();

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(ShowNoGroup));
    partial void OnHasGroupChanged(bool value) => OnPropertyChanged(nameof(ShowNoGroup));

    public async Task LoadAsync()
    {
        EnsureBrowseScope();
        ResetSharedScope();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var scope = CurrentBrowseScope();
        var version = ++_version;
        var today = _clock().Date;
        var model = await RunAsync(() => _composer.Compose(today), "homework");
        if (model is null)
        {
            if (version == _version && operation.IsCurrent && CurrentBrowseScope() == scope)
            { LoadFailed = true; OnPropertyChanged(nameof(ShowBrowseEmpty)); OnPropertyChanged(nameof(BrowseSummary)); }
            return;
        }
        if (version != _version || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        LoadFailed = false;
        HasGroup = model.HasGroup;
        IsLoaded = true;
        _model = model;
        if (_completionUndo is { } undo && !undo.Allows(model.Groups.SelectMany(group => group.Items).FirstOrDefault(item => item.Homework.Id == undo.Id)?.Homework, scope, DateTimeOffset.UtcNow))
            ClearCompletionUndo();
        ApplyFilters();
        Subtitle = $"{App.Loc.Plural(model.Open, "hwOpen1", "hwOpen2", "hwOpen5")} · {T("hwDoneCount", model.Done)}";
        OnPropertyChanged(nameof(Title));
        _ = RefreshSharedTasks();
    }

    private void ApplyFilters()
    {
        Groups.Clear();
        if (_model is { } model)
        foreach (var g in model.Groups)
        {
            var filtered = g with { Items = g.Items.Where(x =>
                HomeworkBrowse.MatchesSubject(x.SubjectRaw, SubjectFilter) &&
                HomeworkBrowse.MatchesStatus(x.Status == "done", StatusFilter) &&
                HomeworkBrowse.MatchesQuery(SearchQuery, x.Subject, x.SubjectRaw, x.Homework.Text)).ToArray() };
            if(filtered.Items.Count>0)Groups.Add(new HomeworkGroupViewModel(filtered, this, collapsed: g.Status == "done" && StatusFilter != 1 && !_expanded.Contains(g.Status)));
        }
        IsEmpty = _model?.HasGroup == true && Groups.Count == 0;
        NotifySharedTasks();
    }

    internal void Toggled(HomeworkGroupViewModel g)
    {
        if (g.IsCollapsed) _expanded.Remove(g.Status); else _expanded.Add(g.Status);
    }

    /// <summary>Two steps: which subject, then the shared homework dialog with due dates counted from today.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Add()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var subjects = await RunAsync(() => _composer.Subjects(), "homework subjects");
        if (subjects is null) return;
        var pick = new SubjectPickerDialogViewModel(subjects);
        if (!await _shell.Dialogs.ShowAsync(pick)) return;
        var subject = pick.Selected ?? new SubjectOption(pick.ManualSubject.Trim(), pick.ManualSubject.Trim(), "");
        if (subject.SubjectRaw.Length == 0) return;
        var groupScope = CurrentBrowseScope();
        var today = _clock().Date;
        var norm = ParityService.NormalizeSubject(subject.SubjectRaw);
        var dues = await RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(norm, today, n)).ToArray(), "homework");
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(subject.Display, nth => dues[Math.Clamp(nth, 1, 10) - 1]);
        dlg.Bind(App);
        dlg.PersistAsync = async () =>
        {
            operation.ThrowIfStale();
            if (CurrentBrowseScope() != groupScope) throw new HomeworkPublicationException("Учебная группа изменилась. Откройте задание заново.");
            var outcome = await HomeworkShare.SaveNewAsync(
                App, subject.SubjectRaw, dlg,
                () => RunAsync(() => App.Db.GetSettings().MyGroupId ?? "", "homework group"),
                async (savedSubject, savedText) =>
                {
                    if (!await RunAsync(() =>
                        HomeworkShare.SaveLocal(App, dlg, savedSubject, savedText, today), "homework add"))
                        throw new LocalHomeworkNotStoredException();
                });
            if (!string.IsNullOrEmpty(outcome.Note)) App.Toasts.Info(outcome.Note);
        };
        if (!await _shell.Dialogs.ShowAsync(dlg)) { App.HomeworkFiles.Discard(dlg.DraftId); return; }
        await ChangedAsync();
    }

    public async Task EditAsync(HomeworkRowViewModel row)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var existing = await RunAsync<Core.Models.Homework>(() => App.Homework.GetById(row.Entry.Homework.Id)!, "homework edit");
        if (existing is null) return;
        var dues = await RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(existing.SubjectRawNormalized, existing.CreatedAt, n)).ToArray(), "homework");
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(row.Subject, nth => dues[Math.Clamp(nth, 1, 10) - 1], existing.Text, existing.TargetNthOccurrence);
        var stored = await RunAsync(() => App.HomeworkFiles.List(existing.Id).ToList(), "homework files");
        if (stored is not null)
            foreach (var file in stored) dlg.Files.Add(new HomeworkAttachment(file.Id, file.Kind, file.Name, false));
        dlg.Bind(App);
        dlg.SavedId = existing.Id;
        dlg.PersistAsync = async () =>
        {
            operation.ThrowIfStale();
            if (!await RunAsync(() =>
                HomeworkShare.SaveLocal(App, dlg, existing.SubjectRawNormalized, dlg.Text.Trim(), existing.CreatedAt), "homework edit"))
                throw new LocalHomeworkNotStoredException();
        };
        if (!await _shell.Dialogs.ShowAsync(dlg)) { App.HomeworkFiles.Discard(dlg.DraftId); return; }
        await ChangedAsync();
    }

    public async Task ToggleDoneAsync(HomeworkRowViewModel row)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        ClearCompletionUndo();
        var scope = CurrentBrowseScope();
        var changed = await RunAsync<HomeworkCompletionUndo>(() =>
        {
            var before = App.Homework.GetById(row.Entry.Homework.Id);
            if (before is null || (before.Status == "done") != row.IsDone) return null!;
            App.Homework.MarkDone(before.Id, !row.IsDone);
            var after = App.Homework.GetById(before.Id);
            return after is null ? null! : HomeworkCompletionUndo.Create(before, after, scope, DateTimeOffset.UtcNow);
        }, "homework done");
        if (changed is null || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        SetCompletionUndo(changed);
        await ChangedAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UndoCompletion()
    {
        var undo = _completionUndo;
        ClearCompletionUndo();
        if (undo is null || undo.Scope != CurrentBrowseScope()) return;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var restored = await RunAsync<Core.Models.Homework>(() =>
        {
            var current = App.Homework.GetById(undo.Id);
            if (!undo.Allows(current, CurrentBrowseScope(), DateTimeOffset.UtcNow)) return null!;
            App.Homework.MarkDone(undo.Id, undo.BeforeDone);
            return App.Homework.GetById(undo.Id)!;
        }, "homework undo");
        if (restored is not null && operation.IsCurrent && undo.Scope == CurrentBrowseScope()) await ChangedAsync();
    }

    public async Task DeleteAsync(HomeworkRowViewModel row)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var confirm = new ConfirmDialogViewModel(T("hwDelete"), T("hwDeleteConfirm", row.Text), T("delete"), danger: true);
        if (!await _shell.Dialogs.ShowAsync(confirm)) return;
        ClearCompletionUndo();
        if (await RunAsync(() =>
            {
                App.Homework.Delete(row.Entry.Homework.Id);
                App.HomeworkFiles.DeleteHomework(row.Entry.Homework.Id);
            }, "homework delete"))
            await ChangedAsync();
    }

    /// <summary>Reload, then tell the schedule cards and the badge. The flag keeps our own _reload (subscribed to
    /// the very HomeworkChanged we raise) from composing the section a second time (T8 #1).</summary>
    private async Task ChangedAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        await LoadAsync();
        _raising = true;
        try { _shell.RaiseHomeworkChanged(); }
        finally { _raising = false; }
        await _shell.UpdateHomeworkBadgeAsync(); // sidebar badge, awaited so nothing outlives this call
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
        Items = group.Items.Select((e, i) => new HomeworkRowViewModel(e, owner, i)).ToList();
        _isCollapsed = collapsed;
    }

    public string Status { get; }
    public string Title { get; }
    public IReadOnlyList<HomeworkRowViewModel> Items { get; }
    public int Count => Items.Count;
    public bool IsDone => Status == "done";

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

    /// <param name="index">Position in the group; drives the appear cascade (Appear.Index).</param>
    public HomeworkRowViewModel(HomeworkEntry entry, HomeworkViewModel owner, int index)
    {
        Entry = entry;
        _owner = owner;
        Index = index;
        Files = owner.App.HomeworkFiles.List(entry.Homework.Id)
            .Select(file => new HomeworkFileLink(file.Id, file.Name)).ToList();
    }

    public HomeworkEntry Entry { get; }
    public int Index { get; }
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
    public IReadOnlyList<HomeworkFileLink> Files { get; }
    public bool HasFiles => Files.Count > 0;

    [RelayCommand] private void OpenFile(string id)
    {
        var path = _owner.App.HomeworkFiles.PathOf(Entry.Homework.Id, id);
        if (path is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { _owner.App.Toasts.Info(_owner.App.Loc.T("hwFileBad")); }
    }

    [RelayCommand] private Task ToggleDone() => _owner.ToggleDoneAsync(this);
    [RelayCommand] private Task Edit() => _owner.EditAsync(this);
    [RelayCommand] private Task Delete() => _owner.DeleteAsync(this);
}

public sealed record HomeworkFileLink(string Id, string Name);
