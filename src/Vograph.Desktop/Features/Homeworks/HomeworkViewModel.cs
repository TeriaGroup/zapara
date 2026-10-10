using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly HomeworkComposer _composer;
    private Func<string, Task>? clipboardWriter;
    public void SetClipboardWriter(Func<string, Task>? writer) => clipboardWriter = writer;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private int _version;
    private bool _raising;
    private readonly HashSet<string> _expanded = new();
    private readonly HashSet<string> _collapsedGroups = new();
    private HomeworkModel? _model;
    public long? HighlightHomeworkId { get; private set; }
    public void OpenPersonalTask(long id)
    {
        overviewSubjectKey = null;
        HighlightHomeworkId = id;
        SubjectFilter = ""; SearchQuery = ""; StatusFilter = 2; DeadlineFilter = 0;
        OriginFilter = 1; FilesOnly = false; SortIndex = 0;
        ApplyFilters(); OnPropertyChanged(nameof(HighlightHomeworkId));
    }
    private string? _browseScope;
    private HomeworkCompletionUndo? _completionUndo;
    private readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private sealed record DuplicateCheck(bool Exists);
    private sealed record NextLessonTarget(DateTime Date, string SubjectRaw, string TimeStart);

    public HomeworkViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _composer = new HomeworkComposer(app);
        BulkPublishRecipients.Changed += BulkPublicationSelectionChanged;
        _undoTimer.Tick += OnUndoTimer;
        _reload = () => { if (!_raising) _ = LoadAsync(); };
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
        shell.HomeworkChanged += _reload;
        app.Loc.LanguageChanged += _reload;
    }

    public override void Detach()
    {
        ClearBulkUndo(); ClearPostponeState(); BulkMode = false;
        ClearBulkPublication(); BulkPublishRecipients.Changed -= BulkPublicationSelectionChanged;
        clipboardWriter = null;
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
    public IAsyncRelayCommand ChangeGroupCommand => _shell.OpenGroupPickerCommand;
    private string CurrentBrowseScope() => App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
    private void EnsureBrowseScope()
    {
        var scope = CurrentBrowseScope();
        if (_browseScope is null) { _browseScope = scope; return; }
        if (_browseScope == scope) return;
        _browseScope = scope;
        _model = null; Groups.Clear(); _expanded.Clear(); _collapsedGroups.Clear(); IsLoaded = false; LoadFailed = false;
        overviewSubjectKey = null; RefreshSubjectOverview();
        HighlightHomeworkId = null; OnPropertyChanged(nameof(HighlightHomeworkId));
        ClearBulkUndo(); ClearPostponeState(); BulkMode = false;
        ClearBulkPublication();
        BulkFeedback = "";
        ClearCompletionUndo();
        SubjectFilter = ""; SearchQuery = ""; StatusFilter = 0;
        DeadlineFilter = 0; OriginFilter = 0; FilesOnly = false; SortIndex = 0;
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
    partial void OnSubjectFilterChanged(string value)
    { if (overviewSubjectKey is not null && !string.Equals(value, SubjectOverview.FirstOrDefault(row => row.SubjectKey == overviewSubjectKey)?.Subject, StringComparison.Ordinal)) overviewSubjectKey = null;
        OnPropertyChanged(nameof(HasSubjectFilter));ApplyFilters();}
    [ObservableProperty] private string searchQuery = "";
    partial void OnSearchQueryChanged(string value) => ApplyFilters();
    [ObservableProperty] private int statusFilter;
    public IReadOnlyList<string> StatusFilters { get; } = ["Активные", "Выполненные", "Все"];
    partial void OnStatusFilterChanged(int value) => ApplyFilters();
    [ObservableProperty] private int deadlineFilter;
    public IReadOnlyList<string> DeadlineFilters { get; } = ["Любой срок", "Просрочено", "Срочно", "Скоро", "Без срока"];
    partial void OnDeadlineFilterChanged(int value) => ApplyFilters();
    [ObservableProperty] private int originFilter;
    public IReadOnlyList<string> OriginFilters { get; } = ["Все задания", "Личные", "Общие"];
    partial void OnOriginFilterChanged(int value) => ApplyFilters();
    [ObservableProperty] private bool filesOnly;
    partial void OnFilesOnlyChanged(bool value) => ApplyFilters();
    [ObservableProperty] private int sortIndex;
    public IReadOnlyList<string> SortOptions { get; } = ["По сроку", "По предмету"];
    partial void OnSortIndexChanged(int value) => ApplyFilters();
    public bool ShowPersonalTasksSection => HasGroup && OriginFilter != 2;
    [RelayCommand] private void ClearSubjectFilter() { overviewSubjectKey = null; SubjectFilter = ""; }
    [RelayCommand] private void ClearBrowseFilters()
    { overviewSubjectKey = null; HighlightHomeworkId = null; OnPropertyChanged(nameof(HighlightHomeworkId)); SubjectFilter = ""; SearchQuery = ""; StatusFilter = 2; DeadlineFilter = 0; OriginFilter = 0; FilesOnly = false; SortIndex = 0; }
    public bool HasBrowseFilters => HighlightHomeworkId is not null || HasSubjectFilter || SearchQuery.Trim().Length > 0 || StatusFilter != 2 ||
        DeadlineFilter != 0 || OriginFilter != 0 || FilesOnly || SortIndex != 0;
    public bool ShowBrowseEmpty => IsLoaded && HasGroup && !LoadFailed && !SharedLoading && (!ShowSharedTasks || SharedLoaded) && Groups.Count == 0 && VisibleSharedTasks.Count == 0;
    public string BrowseEmptyTitle => TotalBrowseCount > 0 ? "По выбранным фильтрам заданий нет" : "Заданий пока нет";
    public string BrowseEmptyHint => TotalBrowseCount > 0 ? "Измените поиск или выберите другой статус." : "Добавьте личное задание или обновите задания группы.";
    /// <summary>#21: счётчик под заголовком — «Открыто: N · Сдано: N», без склонений и сокращений.</summary>
    public string Counter => T("hwOpenDone", _model?.Open ?? 0, _model?.Done ?? 0);
    /// <summary>#21: основная кнопка пустого списка — «Добавить задание», а если пусто из-за фильтров — «Сбросить фильтры».</summary>
    public string BrowseEmptyAction => TotalBrowseCount > 0 ? "Сбросить фильтры" : T("hwAddTask");
    public System.Windows.Input.ICommand BrowseEmptyCommand => TotalBrowseCount > 0 ? ClearBrowseFiltersCommand : AddCommand;
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
    public bool HasGroups => Groups.Count > 0;
    [RelayCommand] private void CollapseAllGroups()
    {
        _expanded.Clear();
        foreach (var group in Groups) { group.IsCollapsed = true; _collapsedGroups.Add(group.Status); }
    }
    [RelayCommand] private void ExpandAllGroups()
    {
        _collapsedGroups.Clear();
        foreach (var group in Groups) { group.IsCollapsed = false; _expanded.Add(group.Status); }
    }

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
        OnPropertyChanged(nameof(Counter));
        if (HighlightHomeworkId is { } target && !model.Groups.SelectMany(group => group.Items).Any(item => item.Homework.Id == target))
        { HighlightHomeworkId = null; OnPropertyChanged(nameof(HighlightHomeworkId)); }
        RefreshSubjectOverview();
        if (_completionUndo is { } undo && !undo.Allows(model.Groups.SelectMany(group => group.Items).FirstOrDefault(item => item.Homework.Id == undo.Id)?.Homework, scope, DateTimeOffset.UtcNow))
            ClearCompletionUndo();
        ApplyFilters();
        Subtitle = $"{T("countOpen", model.Open)} · {T("hwDoneCount", model.Done)}"; // #12: как на web
        OnPropertyChanged(nameof(Title));
        _ = RefreshSharedTasks();
    }

    private void ApplyFilters()
    {
        Groups.Clear();
        if (_model is { } model && OriginFilter != 2)
        foreach (var g in model.Groups)
        {
            var filtered = g with { Items = g.Items.Where(x =>
                (HighlightHomeworkId is null || x.Homework.Id == HighlightHomeworkId) &&
                (overviewSubjectKey is null ? HomeworkBrowse.MatchesSubject(x.SubjectRaw, SubjectFilter) :
                    string.Equals(x.Homework.SubjectRawNormalized, overviewSubjectKey, StringComparison.OrdinalIgnoreCase)) &&
                HomeworkBrowse.MatchesStatus(x.Status == "done", StatusFilter) &&
                HomeworkBrowse.MatchesQuery(SearchQuery, x.Subject, x.SubjectRaw, x.Homework.Text) &&
                HomeworkBrowse.MatchesDeadline(x.Due, _clock().Date, DeadlineFilter) &&
                (!FilesOnly || App.HomeworkFiles.List(x.Homework.Id).Count > 0))
                .OrderBy(x => SortIndex == 1 ? x.Subject : "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Due ?? DateTime.MaxValue).ThenBy(x => x.Homework.CreatedAt).ToArray() };
            if(filtered.Items.Count>0)Groups.Add(new HomeworkGroupViewModel(filtered, this,
                collapsed: _collapsedGroups.Contains(g.Status) || g.Status == "done" && StatusFilter != 1 && !_expanded.Contains(g.Status)));
        }
        IsEmpty = _model?.HasGroup == true && Groups.Count == 0;
        RefreshBulkSelection();
        RefreshFilteredPlanPreview();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(ShowPersonalTasksSection));
        NotifySharedTasks();
    }

    internal void Toggled(HomeworkGroupViewModel g)
    {
        if (g.IsCollapsed) { _expanded.Remove(g.Status); _collapsedGroups.Add(g.Status); }
        else { _expanded.Add(g.Status); _collapsedGroups.Remove(g.Status); }
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
        await OpenNewHomeworkAsync(subject);
    }

    public Task CreateSimilarAsync(HomeworkRowViewModel row)
    {
        if (!row.IsDone || !Groups.SelectMany(group => group.Items).Contains(row)) return Task.CompletedTask;
        return OpenNewHomeworkAsync(new SubjectOption(row.Entry.SubjectRaw, row.Subject, ""), row.Text);
    }
    public async Task CopyHomeworkAsync(HomeworkRowViewModel row)
    {
        if (!Groups.SelectMany(group => group.Items).Contains(row)) return;
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(HomeworkCopyText.Format(row.Subject, row.Text, row.Entry.Due));
            App.Toasts.Info("Задание скопировано.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { App.Toasts.Error("Не удалось скопировать задание."); }
    }
    public async Task OpenNextLessonAsync(HomeworkRowViewModel row)
    {
        if (!Groups.SelectMany(group => group.Items).Contains(row)) return;
        var scope = CurrentBrowseScope();
        var now = _clock();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var target = await RunAsync(() =>
        {
            var settings = App.Db.GetSettings();
            if (string.IsNullOrWhiteSpace(settings.MyGroupId)) return (NextLessonTarget?)null;
            var from = now.Date;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var date = NextOccurrence.Find(App.Db, settings, row.Entry.SubjectRaw, from);
                if (date is null) return null;
                var lesson = App.Schedule.GetSchedule(date.Value, settings.MyGroupId)
                    .Where(item => ParityService.SameSubject(item.SubjectRaw, row.Entry.SubjectRaw))
                    .OrderBy(item => TimeSpan.TryParse(item.TimeStart, out var start) ? start : TimeSpan.Zero)
                    .FirstOrDefault(item => date.Value.Date > now.Date ||
                        !TimeSpan.TryParse(item.TimeEnd, out var end) || end > now.TimeOfDay);
                if (lesson is not null) return new NextLessonTarget(date.Value, lesson.SubjectRaw, lesson.TimeStart);
                from = date.Value.AddDays(1);
            }
            return null;
        }, "homework next lesson");
        if (!operation.IsCurrent || CurrentBrowseScope() != scope) return;
        if (target is null) { App.Toasts.Info("Ближайшая пара этого предмета не найдена в сохранённом расписании."); return; }
        _shell.OpenScheduleAt(target.Date, target.SubjectRaw, target.TimeStart);
    }

    private async Task OpenNewHomeworkAsync(SubjectOption subject, string? initialText = null)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var groupScope = CurrentBrowseScope();
        var today = _clock().Date;
        var norm = ParityService.NormalizeSubject(subject.SubjectRaw);
        var dues = await RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(norm, today, n)).ToArray(), "homework");
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(subject.Display, nth => dues[Math.Clamp(nth, 1, 10) - 1],
            initialText, createSimilar: initialText is not null);
        dlg.Bind(App);
        dlg.PersistAsync = async () =>
        {
            operation.ThrowIfStale();
            if (CurrentBrowseScope() != groupScope) throw new HomeworkPublicationException("Учебная группа изменилась. Откройте задание заново.");
            if (!dlg.AllowDuplicate)
            {
                var duplicate = await RunAsync(() => new DuplicateCheck(HomeworkDuplicateRule.Exists(
                    App.Homework.GetAll(), subject.SubjectRaw, dlg.Text, dues[Math.Clamp(dlg.Nth, 1, 10) - 1])), "homework duplicate");
                if (duplicate is null) throw new HomeworkPublicationException("Не удалось проверить похожие задания. Повторите сохранение.");
                if (duplicate.Exists)
                {
                    dlg.DuplicateWarning = true;
                    throw new HomeworkPublicationException("Похожее задание уже есть. Проверьте текст и срок или создайте ещё одно явно.");
                }
            }
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
        selectedForBulk = owner.IsBulkSelected(entry.Homework.Id);
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
    public bool ShowBulkSelection => _owner.BulkMode;
    internal void RefreshBulkSelectionVisibility() => OnPropertyChanged(nameof(ShowBulkSelection));
    [ObservableProperty] private bool selectedForBulk;
    partial void OnSelectedForBulkChanged(bool value) => _owner.SetBulkSelection(this, value);
    public bool IsApproaching => Entry.Status == "approaching";
    public bool IsBurning => Entry.Status == "burning";
    public bool IsUrgent => Entry.Status == "burning_urgent";
    public bool IsOverdue => Entry.Status == "overdue";
    public bool IsFar => Entry.Status == "far";
    public string DoneLabel => Loc.Current.T(IsDone ? "hwUndo" : "hwMarkDone");
    public IReadOnlyList<HomeworkFileLink> Files { get; }
    public bool HasFiles => Files.Count > 0;
    public bool IsHighlighted => Entry.Homework.Id == _owner.HighlightHomeworkId;

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
    [RelayCommand] private Task CreateSimilar() => _owner.CreateSimilarAsync(this);
    [RelayCommand] private Task CopyText() => _owner.CopyHomeworkAsync(this);
    [RelayCommand] private Task OpenNextLesson() => _owner.OpenNextLessonAsync(this);
}

public sealed record HomeworkFileLink(string Id, string Name);
