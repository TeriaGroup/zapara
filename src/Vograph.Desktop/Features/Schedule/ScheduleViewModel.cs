using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class ScheduleViewModel : ViewModelBase
{
    private readonly ScheduleComposer _composer;
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _onLanguage;
    private readonly Action _onGroup;
    private readonly Action _onSchedule;
    private readonly Action _onHomework;
    private int _reloadVersion;
    private bool _suppressReload;
    private bool _loaded;
    private bool _raising;
    private int? _shownOffset;
    private DateTime _selectedDay;
    private bool _applyingCalendar;
    private int _dateStripCount=7;
    public void SetDateStripCount(int count){if(count is not (5 or 7)||_dateStripCount==count)return;_dateStripCount=count;if(_loaded)_=ReloadAsync();}
    private readonly Avalonia.Threading.DispatcherTimer _planningClock;

    public ScheduleViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _selectedDay = _clock().Date;
        _calendarDate = _selectedDay;
        _planningClock = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _planningClock.Tick += OnPlanningClock;
        _composer = new ScheduleComposer(app);
        _segmentItems = BuildSegmentItems();
        _onLanguage = () => { SegmentItems = BuildSegmentItems(); _ = ReloadAsync(); };
        // Another group: run smart start again — the old offset was chosen for the old group, or for none.
        // Same guard as ReloadAsync: a section that never loaded has no stale offset to correct, and it
        // runs smart start on its own first load — starting Core work here would only outlive the shell.
        _onGroup = () => { if (_loaded) _ = ReloadAsync(); };
        _onSchedule = () => _ = ReloadAsync();
        // Homework changed elsewhere (the Homework section): recompose the day. Our own mutations already
        // reloaded before they raised the event, so _raising keeps the card from composing twice.
        _onHomework = () => { if (!_raising) _ = ReloadAsync(); };
        app.Loc.LanguageChanged += _onLanguage;
        shell.GroupChanged += _onGroup;
        shell.ScheduleChanged += _onSchedule;
        shell.HomeworkChanged += _onHomework;
    }

    public override void Detach()
    {
        _planningClock.Stop();
        _planningClock.Tick -= OnPlanningClock;
        App.Loc.LanguageChanged -= _onLanguage;
        _shell.GroupChanged -= _onGroup;
        _shell.ScheduleChanged -= _onSchedule;
        _shell.HomeworkChanged -= _onHomework;
    }

    /// <summary>Week/Teachers hand over a concrete date; the offset change reloads the day.</summary>
    public override Task ActivateAsync() => _loaded ? ReloadAsync() : InitializeAsync();

    public void ShowDate(DateTime date) => SelectDate(date);

    public ObservableCollection<LessonRowViewModel> Lessons { get; } = new();

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _dayOffset;
    [ObservableProperty] private int _segmentIndex;
    [ObservableProperty] private DateTime? _calendarDate;
    [ObservableProperty] private bool _showFreeTime;
    [ObservableProperty] private bool _hasFreeTime;
    [ObservableProperty] private string _daySummary = "";
    public ObservableCollection<PlannerDayChoice> DateChoices { get; } = [];
    public ObservableCollection<PlannerBreak> FreeTime { get; } = [];
    public ObservableCollection<object> DayRows { get; } = [];
    public bool ShowBreaks => HasFreeTime && ShowFreeTime;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isUnavailable;
    public IAsyncRelayCommand ChangeGroupCommand => _shell.OpenGroupPickerCommand;

    [RelayCommand]
    private async Task Retry()
    {
        await _shell.RefreshScheduleAsync(force: true, quiet: true);
        await ReloadAsync();
    }
    [ObservableProperty] private string? _emptyTitle;
    [ObservableProperty] private string? _emptyHint;
    [ObservableProperty] private bool _showGoToday;

    public DateTime Date { get; private set; }

    private IList<string> BuildSegmentItems() => new[] { T("today"), T("tomorrow"), "Послезавтра" };

    /// <summary>Smart start: today while lessons remain, otherwise tomorrow.</summary>
    public async Task InitializeAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_reloadVersion; // a reload already queued behind the gate must not overwrite the smart-start result
        var now = _clock();
        _selectedDay = now.Date;
        ShowFreeTime = App.Prefs.ShowFreeTime;
        var model = await ComposeAsync(() => _composer.Compose(0, now, _dateStripCount));
        _loaded = true;
        if (model is null || version != _reloadVersion) return;
        _suppressReload = true;
        DayOffset = model.Offset;
        SyncSegment(model.Offset); // DayOffset may already hold that value, and then no change callback ran
        _suppressReload = false;
        Apply(model);
        await LoadDeadlines(model.Date, version);
        _planningClock.Start();
    }

    /// <summary>Recomposes the current day. A no-op before the first load: there is nothing to
    /// refresh yet, and a reload would show "today" instead of the smart-start day.</summary>
    public async Task ReloadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!_loaded) return;
        ShowFreeTime=App.Prefs.ShowFreeTime;
        var version = ++_reloadVersion;
        var now = _clock();
        var offset = (_selectedDay - now.Date).Days;
        _suppressReload = true;
        DayOffset = offset;
        SyncSegment(offset);
        _suppressReload = false;
        var model = await ComposeAsync(() => _composer.Compose(offset, now, _dateStripCount));
        if (model is null || version != _reloadVersion) return; // superseded by a newer reload
        Apply(model);
        await LoadDeadlines(model.Date, version);
    }

    /// <summary>App.CoreGate (inside RunAsync) hands the gate to waiters in order, so awaiting a
    /// compose also awaits the composes queued ahead of it — including the ones a property change
    /// started and dropped.</summary>
    private Task<DayModel?> ComposeAsync(Func<DayModel> work) => RunAsync(work, "schedule");

    /// <summary>Raised after a day is applied: +1 forward, −1 back, 0 for the first day or a reload of the same one.
    /// The view runs the 12px crossfade in that direction.</summary>
    public event Action<int>? DayShown;

    private void Apply(DayModel model)
    {
        if (!CanPublish) return;
        var direction = _shownOffset is { } prev ? Math.Sign(model.Offset - prev) : 0;
        _shownOffset = model.Offset;
        Date = model.Date;
        _selectedDay = model.Date;
        _applyingCalendar = true;
        CalendarDate = model.Date;
        _applyingCalendar = false;
        Title = model.Title;
        Subtitle = model.Subtitle;
        var reconciled = model.Rows.Select((row,index) =>
        {
            var existing = Lessons.FirstOrDefault(x => x.Row.Lesson.SubjectRaw == row.Lesson.SubjectRaw && x.TimeStart == row.TimeStart && x.Row.Lesson.ClassroomRaw == row.Lesson.ClassroomRaw);
            if(existing is null)return new LessonRowViewModel(row,this,index);
            existing.Update(row); return existing;
        }).ToArray();
        for(var i=0;i<reconciled.Length;i++){var old=Lessons.IndexOf(reconciled[i]);if(old<0)Lessons.Insert(i,reconciled[i]);else if(old!=i)Lessons.Move(old,i);}
        while(Lessons.Count>reconciled.Length)Lessons.RemoveAt(Lessons.Count-1);
        OnPropertyChanged(nameof(DayPriorityCaption));OnPropertyChanged(nameof(HasPriority));OnPropertyChanged(nameof(ShowDayState));
        NextStudyDate = model.NextStudyDate;
        SourceSummary = model.SourceSummary;
        IsEmpty = model.Rows.Count == 0;
        IsUnavailable = model.IsUnavailable;
        EmptyTitle = model.EmptyTitle;
        EmptyHint = model.EmptyHint;
        DateChoices.Clear();
        foreach (var day in model.Dates ?? []) DateChoices.Add(new PlannerDayChoice(day, model.Date, this));
        FreeTime.Clear();
        foreach (var gap in model.Breaks ?? []) FreeTime.Add(new PlannerBreak(gap));
        HasFreeTime = FreeTime.Count > 0;
        DaySummary = model.Summary ?? "";
        RebuildDayRows();
        DayShown?.Invoke(direction);
    }

    /// <summary>Segment thumb and the "go to today" pill are pure functions of the offset.</summary>
    private void SyncSegment(int offset)
    {
        SegmentIndex = offset is >= 0 and <= 2 ? offset : -1;
        ShowGoToday = offset != 0;
    }

    partial void OnDayOffsetChanged(int value)
    {
        if (!_suppressReload) _selectedDay = _clock().Date.AddDays(value);
        SyncSegment(value);
        if (!_suppressReload) _ = ReloadAsync();
    }

    partial void OnSegmentIndexChanged(int value)
    {
        if (value is >= 0 and <= 2 && !_suppressReload) SelectDate(_clock().Date.AddDays(value));
    }

    [RelayCommand] private void PrevDay() { if (_selectedDay > DateTime.MinValue.Date) SelectDate(_selectedDay.AddDays(-1)); }
    [RelayCommand] private void NextDay() { if (_selectedDay < DateTime.MaxValue.Date) SelectDate(_selectedDay.AddDays(1)); }
    [RelayCommand] private void GoToday() => SelectDate(_clock().Date);

    public void SelectDate(DateTime date)
    {
        if (_selectedDay != date.Date) { DeadlineFeedback = ""; deadlineUndo = null; }
        _selectedDay = date.Date;
        _suppressReload = true;
        DayOffset = (_selectedDay - _clock().Date).Days;
        SyncSegment(DayOffset);
        _suppressReload = false;
        if (_loaded) _ = ReloadAsync();
    }

    partial void OnCalendarDateChanged(DateTime? value)
    {
        if (!_applyingCalendar && value is { } day) SelectDate(day);
    }

    partial void OnHasFreeTimeChanged(bool value) => OnPropertyChanged(nameof(ShowBreaks));
    partial void OnShowFreeTimeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBreaks));
        RebuildDayRows();
        if (App.Prefs.ShowFreeTime == value) return;
        var previous = App.Prefs.ShowFreeTime;
        App.Prefs.ShowFreeTime = value;
        if (!App.Prefs.Save())
        {
            App.Prefs.ShowFreeTime = previous;
            _showFreeTime = previous;
            OnPropertyChanged(nameof(ShowFreeTime));
            OnPropertyChanged(nameof(ShowBreaks));
            RebuildDayRows();
            App.Toasts.Error("Не удалось сохранить настройку свободного времени.");
        }
    }

    private void OnPlanningClock(object? sender, EventArgs args)
    {
        if (_loaded && ReferenceEquals(_shell.Current, this)) _ = ReloadAsync();
    }

    private void RebuildDayRows()
    {
        var orderedRows = new List<object>();
        var gaps = FreeTime.OrderBy(gap => gap.End).ToArray();
        var nextGap = 0;
        foreach (var row in Lessons)
        {
            if (ShowFreeTime && TimeSpan.TryParse(row.TimeStart, out var start))
                while (nextGap < gaps.Length && gaps[nextGap].End <= start) orderedRows.Add(gaps[nextGap++]);
            orderedRows.Add(row);
        }
        for(var i=0;i<orderedRows.Count;i++){var old=DayRows.IndexOf(orderedRows[i]);if(old<0)DayRows.Insert(i,orderedRows[i]);else if(old!=i)DayRows.Move(old,i);}
        while(DayRows.Count>orderedRows.Count)DayRows.RemoveAt(DayRows.Count-1);
    }

    /// <summary>The card's own name travels with the map (renamed, type stripped), so the Maps header names the lesson.</summary>
    public void ShowMap(LessonRowViewModel row) => _shell.ShowMap(row.Row.Map, row.DisplayName);

    public async Task PickSubgroupAsync(string streamId, string optionId)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var groupId = App.Settings.MyGroupId;
        if (string.IsNullOrEmpty(groupId)) return;
        await RunAsync(() =>
        {
            App.Db.ToggleSubgroupChoice(groupId, streamId, optionId);
            return "";
        }, "subgroup");
        await ReloadAsync();
    }

    public async Task RenameAsync(LessonRowViewModel row)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var l = row.Row.Lesson;
        // RunAsync's T is a non-nullable class, and it already returns null for "no result": "no override" lands there too.
        var existing = await RunAsync<Override>(
            () => (App.Overrides.GetOverride(l.SubjectRaw, "global") ?? App.Overrides.GetOverride(l.SubjectRaw, $"weekday:{l.DayOfWeek}"))!,
            "rename");
        var dlg = new RenameDialogViewModel(LessonText.StripType(l.SubjectRaw, l.TypeRaw), l.SubjectRaw, l.DayOfWeek, existing); // shown stripped; persisted/keyed by the full SubjectRaw
        if (!await _shell.Dialogs.ShowAsync(dlg)) return;

        var ok = await RunAsync(() =>
        {
            if (dlg.ResetRequested)
            {
                foreach (var scope in new[] { "global", $"weekday:{l.DayOfWeek}" })
                    if (App.Overrides.GetOverride(l.SubjectRaw, scope) is { } o) App.Overrides.Remove(o.Id);
            }
            else
            {
                App.Overrides.AddOrUpdate(l.SubjectRaw, dlg.Scope, dlg.EffectiveName, dlg.EffectiveNote);
            }
        }, "rename");
        if (!ok) return;
        App.Toasts.Ok(T("savedOk"));
        await ReloadAsync();
    }

    public async Task AddHomeworkAsync(LessonRowViewModel row)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var l = row.Row.Lesson;
        var norm = ParityService.NormalizeSubject(l.SubjectRaw);
        var today = _clock().Date;
        var dues = await ComputeDuesAsync(norm, today);
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(row.DisplayName, nth => dues[Math.Clamp(nth, 1, 10) - 1]);
        dlg.Bind(App);
        if (!await _shell.Dialogs.ShowAsync(dlg)) { App.HomeworkFiles.Discard(dlg.DraftId); return; }
        long id = 0;
        HomeworkShareOutcome outcome;
        try
        {
            outcome = await HomeworkShare.SaveNewAsync(
                App, l.SubjectRaw, dlg.Text, dlg.Share && !dlg.IsEdit,
                () => RunAsync(() => App.Db.GetSettings().MyGroupId ?? "", "homework group"),
                async (_, body) =>
                {
                    var added = await RunAsync(() => { id = App.Homework.AddHomework(l.SubjectRaw, body, dlg.Nth, createdAt: today); }, "homework add");
                    if (!added) throw new LocalHomeworkNotStoredException();
                    await RunAsync(() => { App.HomeworkFiles.Commit(dlg.DraftId, id, dlg.Removed); }, "homework files");
                });
        }
        catch (LocalHomeworkNotStoredException)
        {
            App.HomeworkFiles.Discard(dlg.DraftId);
            return;
        }
        if (!string.IsNullOrEmpty(outcome.Note)) App.Toasts.Info(outcome.Note);
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    public async Task EditHomeworkAsync(HomeworkItemViewModel hw)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var existing = await RunAsync<Homework>(() => App.Homework.GetById(hw.Id)!, "homework edit"); // null: gone, or the call failed
        if (existing is null) return;
        var dues = await ComputeDuesAsync(existing.SubjectRawNormalized, existing.CreatedAt);
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(hw.Row.DisplayName, nth => dues[Math.Clamp(nth, 1, 10) - 1],
            existing.Text, existing.TargetNthOccurrence);
        var stored = await RunAsync(() => App.HomeworkFiles.List(existing.Id).ToList(), "homework files");
        if (stored is not null)
            foreach (var file in stored) dlg.Files.Add(new HomeworkAttachment(file.Id, file.Kind, file.Name, false));
        dlg.Bind(App);
        if (!await _shell.Dialogs.ShowAsync(dlg)) { App.HomeworkFiles.Discard(dlg.DraftId); return; }
        if (!await RunAsync(() =>
            {
                App.Homework.UpdateHomework(hw.Id, dlg.Text.Trim(), dlg.Nth);
                App.HomeworkFiles.Commit(dlg.DraftId, existing.Id, dlg.Removed);
            }, "homework edit"))
        {
            App.HomeworkFiles.Discard(dlg.DraftId);
            return;
        }
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    /// <summary>Every due date the stepper can show, computed once off the UI thread: the dialog then
    /// only indexes the table, so changing N never touches SQLite from the UI thread.</summary>
    private Task<DateTime?[]?> ComputeDuesAsync(string subjectNormalized, DateTime from) =>
        RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(subjectNormalized, from, n)).ToArray(), "homework");

    public async Task ToggleDoneAsync(HomeworkItemViewModel hw)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!await RunAsync(() => App.Homework.MarkDone(hw.Id, !hw.IsDone), "homework done")) return;
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    public async Task DeleteHomeworkAsync(HomeworkItemViewModel hw)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var confirm = new ConfirmDialogViewModel(T("hwDelete"), T("hwDeleteConfirm", hw.Text), T("delete"), danger: true);
        if (!await _shell.Dialogs.ShowAsync(confirm)) return;
        if (!await RunAsync(() => { App.Homework.Delete(hw.Id); App.HomeworkFiles.DeleteHomework(hw.Id); }, "homework delete")) return;
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    /// <summary>Tells the Homework section and the sidebar badge about a card-side mutation; the flag keeps
    /// our own _onHomework handler from starting a second compose of the day we just reloaded. Awaited by
    /// every caller so the badge refresh's Core read finishes before the caller's own Task completes, instead
    /// of racing a test's teardown Dispose of the SQLite connection it reads from.</summary>
    private async Task RaiseHomeworkAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        _raising = true;
        try { _shell.RaiseHomeworkChanged(); }
        finally { _raising = false; }
        await _shell.UpdateHomeworkBadgeAsync();
    }
}
