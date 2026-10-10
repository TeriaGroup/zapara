using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Vograph.Core.Models;
using Vograph.Core.Campus;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class ScheduleViewModel : ViewModelBase
{
    private readonly ScheduleComposer _composer;
    private readonly ShellViewModel _shell;
    private Func<string, Task>? clipboardWriter;
    public void SetClipboardWriter(Func<string, Task>? writer) => clipboardWriter = writer;
    private readonly Func<DateTime> _clock;
    private readonly Action _onLanguage;
    private readonly Action _onGroup;
    private readonly Action _onSchedule;
    private readonly Action _onHomework;
    private bool transferGraphLoaded;
    private CampusGraph? transferGraph;
    private int _reloadVersion;
    private bool _suppressReload;
    private bool _loaded;
    [ObservableProperty] private bool loadingDay;
    [ObservableProperty] private string scheduleLoadError = "";
    public bool ShowFirstDayLoading => LoadingDay && !_loaded;
    partial void OnLoadingDayChanged(bool value) => OnPropertyChanged(nameof(ShowFirstDayLoading));
    private bool _raising;
    private int? _shownOffset;
    private DateTime _selectedDay;
    private bool _applyingCalendar;
    private int _dateStripCount=7;
    public void SetDateStripCount(int count){if(count is not (5 or 7)||_dateStripCount==count)return;_dateStripCount=count;if(_loaded)_=ReloadAsync();}
    private readonly Avalonia.Threading.DispatcherTimer _planningClock;
    private readonly DispatcherTimer _subgroupUndoClock = new() { Interval = TimeSpan.FromSeconds(5) };
    private SubgroupChoiceUndo? subgroupUndo;
    private int subgroupRenderEpoch;
    internal int SubgroupRenderEpoch => subgroupRenderEpoch;
    private string SubgroupScope => App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
    public bool HasSubgroupUndo => subgroupUndo is { } undo && undo.Scope == SubgroupScope && DateTimeOffset.UtcNow < undo.ExpiresAt;
    public string SubgroupUndoCaption => subgroupUndo is { Before: null } ? "Подгруппа выбрана" : "Выбор подгруппы изменён";
    private void ClearSubgroupUndo()
    {
        _subgroupUndoClock.Stop(); subgroupUndo = null;
        OnPropertyChanged(nameof(HasSubgroupUndo)); OnPropertyChanged(nameof(SubgroupUndoCaption));
    }
    private void SetSubgroupUndo(SubgroupChoiceUndo undo)
    {
        _subgroupUndoClock.Stop(); subgroupUndo = undo;
        var remaining = undo.ExpiresAt - DateTimeOffset.UtcNow;
        _subgroupUndoClock.Interval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
        _subgroupUndoClock.Start();
        OnPropertyChanged(nameof(HasSubgroupUndo)); OnPropertyChanged(nameof(SubgroupUndoCaption));
    }

    public ScheduleViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _selectedDay = _clock().Date;
        _calendarDate = _selectedDay;
        _planningClock = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _planningClock.Tick += OnPlanningClock;
        _subgroupUndoClock.Tick += (_, _) => ClearSubgroupUndo();
        _composer = new ScheduleComposer(app);
        _segmentItems = BuildSegmentItems();
        _onLanguage = () => { SegmentItems = BuildSegmentItems(); _ = ReloadAsync(); };
        // Another group: run smart start again — the old offset was chosen for the old group, or for none.
        // Same guard as ReloadAsync: a section that never loaded has no stale offset to correct, and it
        // runs smart start on its own first load — starting Core work here would only outlive the shell.
        _onGroup = () => { subgroupRenderEpoch++; pendingLessonFocus = null; LessonSearch = ""; ClearSubgroupUndo(); if (_loaded) _ = InitializeAsync(smartStart: true); };
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
        clipboardWriter = null;
        _planningClock.Stop();
        _subgroupUndoClock.Stop();
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
    public bool CanOpenTeacher(LessonRowViewModel row)
    {
        var name = row.Row.Teacher.Trim();
        return name.Length > 0 && !name.Contains(';') && App.Lecturers.IsLoaded &&
            App.Lecturers.Lecturers.Count(info => TeacherSearch.SameTeacher(info.Name, name)) == 1;
    }
    public async Task OpenTeacherAsync(LessonRowViewModel row)
    {
        if (!Lessons.Contains(row) || !CanOpenTeacher(row)) return;
        var name = row.Row.Teacher.Trim();
        var match = App.Lecturers.Lecturers.Single(info => TeacherSearch.SameTeacher(info.Name, name));
        var group = App.Settings.MyGroupId; var scope = App.Profile.DatabasePath;
        var teachers = _shell.Section<TeachersViewModel>(SectionKey.Teachers);
        await teachers.OpenByNameAsync(match.Name);
        if (App.Work.CanPublish && scope == App.Profile.DatabasePath && group == App.Settings.MyGroupId &&
            teachers.Selected?.Info.Id == match.Id) _shell.NavigateTo(SectionKey.Teachers);
    }
    public event Action<LessonRowViewModel>? LessonFocusRequested;
    private sealed record LessonFocusRequest(DateTime Date, string Subject, string Time,
        string? End, string? Type, string? Teacher, string? Classroom);
    private LessonFocusRequest? pendingLessonFocus;
    public void RequestLessonFocus(DateTime date, string subjectRaw, string timeStart,
        string? timeEnd = null, string? typeRaw = null, string? teacherRaw = null, string? classroomRaw = null)
    {
        pendingLessonFocus = new(date.Date, subjectRaw, timeStart, timeEnd, typeRaw, teacherRaw, classroomRaw);
        if (_loaded && Date.Date == date.Date) ApplyPendingLessonFocus();
    }
    private void ApplyPendingLessonFocus(bool final = false)
    {
        if (pendingLessonFocus is not { } request) return;
        if (Date.Date != request.Date) return;
        var matches = Lessons.Where(item => item.Row.Lesson.SubjectRaw == request.Subject && item.TimeStart == request.Time &&
            (request.End is null || item.TimeEnd == request.End) &&
            (request.Type is null || item.Row.Lesson.TypeRaw == request.Type) &&
            (request.Teacher is null || item.Row.Lesson.TeacherRaw == request.Teacher) &&
            (request.Classroom is null || item.Row.Lesson.ClassroomRaw == request.Classroom)).Take(2).ToArray();
        if (matches.Length != 1)
        { if (final) { pendingLessonFocus = null; App.Toasts.Info("Пара изменилась в сохранённом расписании. Проверьте день заново."); }
            return; }
        pendingLessonFocus = null;
        var row = matches[0];
        row.ShowDetails = true;
        LessonFocusRequested?.Invoke(row);
    }
    public ObservableCollection<ScheduleOverlap> Overlaps { get; } = new();
    [RelayCommand] private void OpenOverlapFirst(ScheduleOverlap? overlap) => OpenOverlapSide(overlap, true);
    [RelayCommand] private void OpenOverlapSecond(ScheduleOverlap? overlap) => OpenOverlapSide(overlap, false);
    private void OpenOverlapSide(ScheduleOverlap? overlap, bool first)
    {
        if (overlap is null || !Overlaps.Contains(overlap)) return;
        var row = first ? overlap.First : overlap.Second;
        if (!Lessons.Contains(row)) return;
        row.ShowDetails = true; LessonFocusRequested?.Invoke(row);
    }
    [ObservableProperty] private IReadOnlyList<string> transferWarnings = [];
    public bool HasTransferWarnings => TransferWarnings.Count > 0;
    partial void OnTransferWarningsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasTransferWarnings));
    public bool HasOverlaps => Overlaps.Count > 0;

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _dayOffset;
    [ObservableProperty] private int _segmentIndex;
    [ObservableProperty] private DateTime? _calendarDate;
    [ObservableProperty] private string _daySummary = "";
    [ObservableProperty] private string _workloadSpan = "";
    public ObservableCollection<PlannerDayChoice> DateChoices { get; } = [];
    public ObservableCollection<PlannerBreak> FreeTime { get; } = [];
    public ObservableCollection<object> DayRows { get; } = [];
    [ObservableProperty] private bool remainingToday;
    public bool CanFilterRemainingToday => Date.Date == _clock().Date && Lessons.Count > 0;
    public IReadOnlyList<object> VisibleDayRows => !RemainingToday || !CanFilterRemainingToday
        ? DayRows.ToArray()
        : DayRows.Where(item => item switch
        {
            LessonRowViewModel lesson => !lesson.IsPast,
            PlannerBreak gap => gap.End > _clock().TimeOfDay,
            _ => false
        }).ToArray();
    public bool RemainingEmpty => RemainingToday && CanFilterRemainingToday &&
        !VisibleDayRows.OfType<LessonRowViewModel>().Any();
    partial void OnRemainingTodayChanged(bool value) => RefreshRemainingToday();
    private void RefreshRemainingToday()
    {
        OnPropertyChanged(nameof(CanFilterRemainingToday));
        OnPropertyChanged(nameof(VisibleDayRows));
        OnPropertyChanged(nameof(RemainingEmpty));
    }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    /// <summary>#12: заголовок страницы — «Расписание», как на web; относительный день («Сегодня») — в начале этой строки.</summary>
    public string DayLine => string.IsNullOrWhiteSpace(Subtitle) ? Title : Subtitle.StartsWith(Title, StringComparison.OrdinalIgnoreCase) ? Subtitle : $"{Title} · {Subtitle}";
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DayLine));
    partial void OnSubtitleChanged(string value) => OnPropertyChanged(nameof(DayLine));
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isUnavailable;
    /// <summary>#21: группа не выбрана — пустое состояние с кнопкой «Выбрать группу» вместо «нажмите слева».</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EmptyAction))] private bool _needsGroup;
    public string? EmptyAction => NeedsGroup ? T("chooseGroup") : null;
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
    public Task InitializeAsync() => InitializeAsync(smartStart: false);

    private async Task InitializeAsync(bool smartStart)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_reloadVersion; // a reload already queued behind the gate must not overwrite the smart-start result
        LoadingDay = true;
        var now = _clock();
        _selectedDay = now.Date;
        var model = await ComposeAsync(() => _composer.Compose(smartStart ? _composer.InitialOffset(now) : 0,
            now, _dateStripCount));
        if (version == _reloadVersion && operation.IsCurrent) LoadingDay = false;
        if (_selectedDay != now.Date)
        {
            _loaded = true;
            await ReloadAsync();
            _planningClock.Start();
            return;
        }
        _loaded = true;
        if (model is null || version != _reloadVersion)
        { if (model is null && version == _reloadVersion) ScheduleLoadError = "День не загрузился. Последняя доступная карточка сохранена."; return; }
        ScheduleLoadError = "";
        _suppressReload = true;
        DayOffset = model.Offset;
        SyncSegment(model.Offset); // DayOffset may already hold that value, and then no change callback ran
        _suppressReload = false;
        Apply(model);
        await LoadDeadlines(model.Date, version);
        await LoadTransferWarnings(model.Rows, version);
        _planningClock.Start();
    }

    /// <summary>Recomposes the current day. A no-op before the first load: there is nothing to
    /// refresh yet, and a reload would show "today" instead of the smart-start day.</summary>
    public async Task ReloadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!_loaded) return;
        var version = ++_reloadVersion;
        LoadingDay = true;
        var now = _clock();
        var offset = (_selectedDay - now.Date).Days;
        _suppressReload = true;
        DayOffset = offset;
        SyncSegment(offset);
        _suppressReload = false;
        var model = await ComposeAsync(() => _composer.Compose(offset, now, _dateStripCount));
        if (version == _reloadVersion && operation.IsCurrent) LoadingDay = false;
        if (model is null || version != _reloadVersion)
        { if (model is null && version == _reloadVersion) ScheduleLoadError = "День не загрузился. Последняя доступная карточка сохранена."; return; }
        ScheduleLoadError = "";
        Apply(model);
        await LoadDeadlines(model.Date, version);
        await LoadTransferWarnings(model.Rows, version);
    }

    private sealed record TransferWarningResult(IReadOnlyList<string> Warnings);
    private async Task LoadTransferWarnings(IReadOnlyList<LessonRow> rows, int version)
    {
        if (version != _reloadVersion) return;
        if (rows.Count < 2) { TransferWarnings = []; return; }
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var result = await RunAsync(() =>
        {
            if (!transferGraphLoaded)
            {
                transferGraphLoaded = true;
                try
                {
                    var path = Path.Combine(App.Maps.BundledDir, "campus-graph.json");
                    if (File.Exists(path)) transferGraph = CampusGraph.Load(File.ReadAllText(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CampusGraphException)
                { transferGraph = null; }
            }
            return new TransferWarningResult(ScheduleTransitionPlanner.Warnings(transferGraph, rows));
        }, "schedule transfer");
        if (result is not null && operation.IsCurrent && version == _reloadVersion)
            TransferWarnings = result.Warnings;
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
        if (Date.Date != _clock().Date) RemainingToday = false;
        _selectedDay = model.Date;
        _applyingCalendar = true;
        CalendarDate = model.Date;
        _applyingCalendar = false;
        Title = model.Title;
        Subtitle = model.Subtitle;
        var usedRows = new HashSet<LessonRowViewModel>();
        var reconciled = model.Rows.Select((row,index) =>
        {
            var existing = Lessons.FirstOrDefault(x => !usedRows.Contains(x) &&
                x.Row.Lesson.SubjectRaw == row.Lesson.SubjectRaw && x.TimeStart == row.TimeStart &&
                x.Row.Lesson.ClassroomRaw == row.Lesson.ClassroomRaw);
            if (existing is null) return new LessonRowViewModel(row, this, index);
            usedRows.Add(existing);
            existing.Update(row); return existing;
        }).ToArray();
        for(var i=0;i<reconciled.Length;i++){var old=Lessons.IndexOf(reconciled[i]);if(old<0)Lessons.Insert(i,reconciled[i]);else if(old!=i)Lessons.Move(old,i);}
        while(Lessons.Count>reconciled.Length)Lessons.RemoveAt(Lessons.Count-1);
        OnPropertyChanged(nameof(HasLessons));
        RefreshLessonSearch();
        ApplyPendingLessonFocus(final: true);
        Overlaps.Clear(); foreach (var conflict in ScheduleOverlap.Find(Lessons.ToArray())) Overlaps.Add(conflict);
        OnPropertyChanged(nameof(HasOverlaps));
        OnPropertyChanged(nameof(DayPriorityCaption));OnPropertyChanged(nameof(HasPriority));OnPropertyChanged(nameof(ShowDayState));
        NextStudyDate = model.NextStudyDate;
        SourceSummary = model.SourceSummary;
        IsEmpty = model.Rows.Count == 0;
        IsUnavailable = model.IsUnavailable;
        NeedsGroup = model.NeedsGroup;
        EmptyTitle = model.EmptyTitle;
        EmptyHint = model.EmptyHint;
        DateChoices.Clear();
        foreach (var day in model.Dates ?? []) DateChoices.Add(new PlannerDayChoice(day, model.Date, this));
        FreeTime.Clear();
        foreach (var gap in model.Breaks ?? []) FreeTime.Add(new PlannerBreak(gap));
        DaySummary = model.Summary ?? "";
        var starts = model.Rows.Select(row => TimeSpan.TryParse(row.TimeStart, out var time) ? time : (TimeSpan?)null)
            .Where(time => time is not null).Select(time => time!.Value).ToArray();
        var ends = model.Rows.Select(row => TimeSpan.TryParse(row.TimeEnd, out var time) ? time : (TimeSpan?)null)
            .Where(time => time is not null).Select(time => time!.Value).ToArray();
        WorkloadSpan = starts.Length == 0 || ends.Length == 0 ? "" :
            $"С {starts.Min():hh\\:mm} до {ends.Max():hh\\:mm} · перерывы: {(model.Breaks?.Sum(gap => gap.Minutes) ?? 0)} мин";
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
    [RelayCommand] private async Task CopyDay()
    {
        if (!_loaded) return;
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        var text = ScheduleShareText.Format(Date, Lessons.Select(row =>
            (row.TimeStart, row.Row.TimeEnd, row.DisplayName, row.RoomText, row.Row.Teacher)));
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(text);
            if (App.Work.CanPublish && scope == App.Profile.DatabasePath + ":" + App.Settings.MyGroupId)
                App.Toasts.Info("День скопирован.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { App.Toasts.Error("Не удалось скопировать день."); }
    }

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
            if (TimeSpan.TryParse(row.TimeStart, out var start))
                while (nextGap < gaps.Length && gaps[nextGap].End <= start) orderedRows.Add(gaps[nextGap++]);
            orderedRows.Add(row);
        }
        for(var i=0;i<orderedRows.Count;i++){var old=DayRows.IndexOf(orderedRows[i]);if(old<0)DayRows.Insert(i,orderedRows[i]);else if(old!=i)DayRows.Move(old,i);}
        while(DayRows.Count>orderedRows.Count)DayRows.RemoveAt(DayRows.Count-1);
        RefreshRemainingToday();
    }

    /// <summary>The card's own name travels with the map (renamed, type stripped), so the Maps header names the lesson.</summary>
    public void ShowMap(LessonRowViewModel row) => _shell.ShowMap(row.Row.Map, row.DisplayName, Date);

    public async Task PickSubgroupAsync(string sourceGroupId, string sourceScope, int renderEpoch, string streamId, string optionId)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var groupId = App.Settings.MyGroupId;
        if (string.IsNullOrEmpty(groupId) || groupId != sourceGroupId || SubgroupScope != sourceScope || renderEpoch != subgroupRenderEpoch) return;
        var scope = SubgroupScope;
        ClearSubgroupUndo();
        var changed = await RunAsync<SubgroupChoiceUndo>(() =>
        {
            if (SubgroupScope != scope || App.Settings.MyGroupId != sourceGroupId || renderEpoch != subgroupRenderEpoch) return null!;
            var stream = SubgroupRules.Build(App.Db.GetAllLessonsForGroup(groupId)).Streams.FirstOrDefault(row => row.Id == streamId);
            if (stream is null || stream.Options.All(option => option.Id != optionId)) return null!;
            var before = App.Db.GetSubgroupChoices(groupId).GetValueOrDefault(streamId);
            App.Db.ToggleSubgroupChoice(groupId, streamId, optionId);
            var after = App.Db.GetSubgroupChoices(groupId).GetValueOrDefault(streamId);
            return new SubgroupChoiceUndo(scope, streamId, before, after, DateTimeOffset.UtcNow.AddSeconds(5));
        }, "subgroup");
        if (changed is null || !operation.IsCurrent || SubgroupScope != scope) return;
        SetSubgroupUndo(changed);
        await ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UndoSubgroup()
    {
        var undo = subgroupUndo;
        ClearSubgroupUndo();
        if (undo is null || undo.Scope != SubgroupScope) return;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var restored = await RunAsync(() =>
        {
            if (SubgroupScope != undo.Scope) return "";
            var group = App.Settings.MyGroupId;
            if (string.IsNullOrEmpty(group)) return "";
            var stream = SubgroupRules.Build(App.Db.GetAllLessonsForGroup(group)).Streams.FirstOrDefault(row => row.Id == undo.StreamId);
            if (stream is null || undo.Before is { } prior && stream.Options.All(option => option.Id != prior)) return "";
            var current = App.Db.GetSubgroupChoices(group).GetValueOrDefault(undo.StreamId);
            if (!undo.Allows(SubgroupScope, current, DateTimeOffset.UtcNow)) return "";
            App.Db.ToggleSubgroupChoice(group, undo.StreamId, undo.Before ?? current!);
            return "restored";
        }, "subgroup undo");
        if (restored == "restored" && operation.IsCurrent && SubgroupScope == undo.Scope) await ReloadAsync();
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
        dlg.PersistAsync = async () =>
        {
            operation.ThrowIfStale();
            var outcome = await HomeworkShare.SaveNewAsync(
                App, l.SubjectRaw, dlg,
                () => RunAsync(() => App.Db.GetSettings().MyGroupId ?? "", "homework group"),
                async (subject, body) =>
                {
                    if (!await RunAsync(() =>
                        HomeworkShare.SaveLocal(App, dlg, subject, body, today), "homework add"))
                        throw new LocalHomeworkNotStoredException();
                });
            if (!string.IsNullOrEmpty(outcome.Note)) App.Toasts.Info(outcome.Note);
        };
        if (!await _shell.Dialogs.ShowAsync(dlg)) { App.HomeworkFiles.Discard(dlg.DraftId); return; }
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
        dlg.SavedId = existing.Id;
        dlg.PersistAsync = async () =>
        {
            operation.ThrowIfStale();
            if (!await RunAsync(() =>
                HomeworkShare.SaveLocal(App, dlg, existing.SubjectRawNormalized, dlg.Text.Trim(), existing.CreatedAt), "homework edit"))
                throw new LocalHomeworkNotStoredException();
        };
        if (!await _shell.Dialogs.ShowAsync(dlg)) { App.HomeworkFiles.Discard(dlg.DraftId); return; }
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
