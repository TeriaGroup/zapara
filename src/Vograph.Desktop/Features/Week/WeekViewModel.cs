using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Week;

public sealed partial class WeekViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly WeekComposer _composer;
    private Func<string, Task>? clipboardWriter;
    public void SetClipboardWriter(Func<string, Task>? writer) => clipboardWriter = writer;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private readonly Action _scheduleReload;
    private readonly Action _groupReload;
    private int _version;
    private bool _suppress;
    private DateTime _selectedDate;
    private readonly HashSet<DateTime> _collapsedDays = [];

    public WeekViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _selectedDate = _clock().Date;
        AssessmentStartDate = _selectedDate;
        _composer = new WeekComposer(app);
        _segmentItems = new[] { T("weekOdd"), T("weekEven") };
        _reload = () => _ = ReloadAsync();
        _scheduleReload = CaptureAndReloadAfterScheduleChange;
        _groupReload = () => { _collapsedDays.Clear(); ClearRefreshDiff(); _ = ReloadAsync(); };
        app.Loc.LanguageChanged += _reload;
        shell.GroupChanged += _groupReload;
        shell.ScheduleChanged += _scheduleReload;
        shell.HomeworkChanged += _reload;
    }

    public override void Detach()
    {
        clipboardWriter = null;
        App.Loc.LanguageChanged -= _reload;
        _shell.GroupChanged -= _groupReload;
        _shell.ScheduleChanged -= _scheduleReload;
        _shell.HomeworkChanged -= _reload;
    }

    public override Task ActivateAsync() => ReloadAsync();

    public string Title => T("navWeek");
    public ObservableCollection<WeekDayViewModel> Days { get; } = new();
    public IReadOnlyList<WeekDeadlineViewModel> UnknownDeadlines { get; private set; } = [];
    public bool HasUnknownDeadlines => UnknownDeadlines.Count > 0;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _onlyDaysWithClasses;
    [ObservableProperty] private IReadOnlyList<WeekDayViewModel> _visibleDays = [];
    [ObservableProperty] private int weekColumns = 3;
    [ObservableProperty] private bool isLoading;
    public bool ShowFirstLoading => IsLoading && !IsLoaded;
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowFirstLoading));
    internal void SetViewportWidth(double width) => WeekColumns = width < 620 ? 1 : width < 980 ? 2 : 3;
    public bool HasBrowseFilters => SearchQuery.Trim().Length > 0 || OnlyDaysWithClasses;
    public bool NoWeekMatches => ShowWeekCards && VisibleDays.Count == 0 && HasBrowseFilters;
    public string BrowseCount => $"Показано дней: {VisibleDays.Count} из {Days.Count}";
    partial void OnSearchQueryChanged(string value) => RefreshBrowse();
    partial void OnOnlyDaysWithClassesChanged(bool value) => RefreshBrowse();
    [RelayCommand] private void ClearBrowseFilters() { SearchQuery = ""; OnlyDaysWithClasses = false; }
    [RelayCommand] private void OpenFirstStudyDay()
    {
        var first = Days.FirstOrDefault(day => !day.IsEmpty);
        if (first is not null) OpenDay(first);
    }
    public bool HasStudyDay => Days.Any(day => !day.IsEmpty);

    private void RefreshBrowse()
    {
        VisibleDays = WeekBrowse.Filter(Days.Select(day => day.Day), SearchQuery, OnlyDaysWithClasses)
            .Select(day => new WeekDayViewModel(day, this,
                SearchQuery.Trim().Length == 0 && _collapsedDays.Contains(day.Date.Date))).ToArray();
        OnPropertyChanged(nameof(HasBrowseFilters));
        OnPropertyChanged(nameof(NoWeekMatches));
        OnPropertyChanged(nameof(BrowseCount));
    }

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _parityIndex; // 0 odd, 1 even; lands on the current week on the first load
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _hasGroup;
    [ObservableProperty] private bool _hasCopy;
    [ObservableProperty] private string _weekRange = "";
    [ObservableProperty] private DateTime? _calendarWeekDate;
    partial void OnCalendarWeekDateChanged(DateTime? value)
    {
        if (_suppress || value is null || value.Value.Date == _selectedDate.Date) return;
        ClearRefreshDiff();
        _selectedDate = value.Value.Date;
        _ = ReloadAsync();
    }
    [ObservableProperty] private string _loadError = "";
    public bool ShowNoGroup => IsLoaded && !HasGroup;
    public bool ShowNoCopy => IsLoaded && HasGroup && !HasCopy;
    public bool ShowWeekCards => HasGroup && HasCopy;
    partial void OnIsLoadedChanged(bool value) { OnPropertyChanged(nameof(ShowNoGroup)); OnPropertyChanged(nameof(ShowNoCopy)); }
    partial void OnHasGroupChanged(bool value) { OnPropertyChanged(nameof(ShowNoGroup)); OnPropertyChanged(nameof(ShowNoCopy)); OnPropertyChanged(nameof(ShowWeekCards)); }
    partial void OnHasCopyChanged(bool value) { OnPropertyChanged(nameof(ShowNoCopy)); OnPropertyChanged(nameof(ShowWeekCards)); }

    partial void OnParityIndexChanged(int value)
    {
        if (_suppress) return;
        ClearRefreshDiff();
        _selectedDate = _selectedDate.AddDays(7);
        _ = ReloadAsync();
    }

    [RelayCommand] private Task PreviousWeek() { ClearRefreshDiff(); _selectedDate = _selectedDate.AddDays(-7); return ReloadAsync(); }
    [RelayCommand] private Task NextWeek() { ClearRefreshDiff(); _selectedDate = _selectedDate.AddDays(7); return ReloadAsync(); }
    [RelayCommand] private Task CurrentWeek() { ClearRefreshDiff(); _selectedDate = _clock().Date; return ReloadAsync(); }
    [RelayCommand] private void ChooseGroup() => _shell.NavigateTo(SectionKey.Settings);
    [RelayCommand] private async Task CopyWeek()
    {
        if (!ShowWeekCards || Days.Count == 0) return;
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        var text = WeekShareText.Format(Days.Select(day => day.Day));
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(text);
            if (App.Work.CanPublish && scope == App.Profile.DatabasePath + ":" + App.Settings.MyGroupId)
                App.Toasts.Info("Неделя скопирована.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { App.Toasts.Error("Не удалось скопировать неделю."); }
    }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ExportWeek()
    {
        if (!ShowWeekCards || Days.Count != 7) return;
        var monday = _selectedDate.Date.AddDays(-((int)_selectedDate.DayOfWeek + 6) % 7);
        if (Days[0].Date != monday)
        { App.Toasts.Info("Дождитесь загрузки выбранной недели."); return; }
        var group = App.Settings.MyGroupId;
        var scope = App.Profile.DatabasePath + ":" + group;
        var batch = WeekCalendarEntries.Create(Days.Select(day => day.Day), group);
        var result = CalendarExport.Create(batch.Entries, $"Расписание военмех · {WeekRange}", DateTimeOffset.UtcNow);
        if (result.EventCount == 0) { App.Toasts.Info("На выбранной неделе нет пар с корректным временем."); return; }
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var path = await App.FileDialogs.SaveCalendarAsync($"raspisanie-voenmeh-{monday:yyyyMMdd}.ics");
        if (path is null || !operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId ||
            _selectedDate.Date.AddDays(-((int)_selectedDate.DayOfWeek + 6) % 7) != monday) return;
        try
        {
            await File.WriteAllTextAsync(path, result.Content, new UTF8Encoding(false), operation.Token);
            if (operation.IsCurrent) App.Toasts.Info($"Сохранено занятий: {result.EventCount}; пропущено: {result.SkippedCount + batch.SkippedCount}.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (operation.IsCurrent) App.Toasts.Error("Не удалось сохранить календарь."); }
    }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RetryWeek()
    {
        await _shell.RefreshScheduleAsync(force: true, quiet: false);
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        IsLoading = true;
        var today = _clock().Date;
        var selected = _selectedDate;
        var model = await RunAsync(() => _composer.ComposeCalendar(selected, today), "week");
        if (version == _version && operation.IsCurrent) IsLoading = false;
        if (model is null)
        {
            if (version == _version && operation.IsCurrent) LoadError = "Неделю не удалось открыть. Последняя загруженная неделя сохранена.";
            return;
        }
        if (version != _version || !operation.IsCurrent || selected != _selectedDate) return;
        LoadError = "";
        ApplyRefreshDiff(model, model.WeekStart ?? selected.Date.AddDays(-((int)selected.DayOfWeek + 6) % 7));
        _suppress = true;
        ParityIndex = model.Parity == 1 ? 0 : 1;
        CalendarWeekDate = selected;
        _suppress = false;
        Apply(model);
        lastRenderedStamp = model.HasGroup && model.HasCopy ? ReadWeekSourceStamp() : null;
    }

    private void Apply(WeekModel m)
    {
        HasGroup = m.HasGroup;
        HasCopy = m.HasCopy;
        IsLoaded = true;
        var suffix = T("weekCurrentSuffix");
        SegmentItems = new[] { T("weekOdd") + (m.IsOddToday ? suffix : ""), T("weekEven") + (m.IsOddToday ? "" : suffix) };
        Subtitle = $"{T("parityWeek", App.I18n.FormatParity(m.Parity == 1))} · {App.Loc.Plural(m.Total, "lessons1", "lessons2", "lessons5")}";
        var monday = m.WeekStart ?? _selectedDate.Date.AddDays(-((int)_selectedDate.DayOfWeek + 6) % 7);
        ValidateWeekComparison(monday);
        WeekRange = $"{monday.ToString("d MMMM", CultureInfo.GetCultureInfo("ru-RU"))} — {monday.AddDays(6).ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"))}";
        Days.Clear();
        foreach (var d in m.Days) Days.Add(new WeekDayViewModel(d, this, _collapsedDays.Contains(d.Date.Date)));
        UnknownDeadlines = (m.UnknownDeadlines ?? []).Select(deadline => new WeekDeadlineViewModel(deadline, this)).ToArray();
        OnPropertyChanged(nameof(UnknownDeadlines)); OnPropertyChanged(nameof(HasUnknownDeadlines));
        RefreshBrowse();
        OnPropertyChanged(nameof(HasStudyDay));
        OnPropertyChanged(nameof(Title));
    }

    public void OpenDay(WeekDayViewModel day) => _shell.OpenScheduleAt(day.Date);
    internal void ToggleDay(DateTime date)
    {
        if (SearchQuery.Trim().Length > 0) return;
        if (!_collapsedDays.Add(date.Date)) _collapsedDays.Remove(date.Date);
        RefreshBrowse();
    }
    internal void OpenLesson(DateTime date, WeekRow row)
    {
        if (string.IsNullOrWhiteSpace(row.SubjectRaw)) _shell.OpenScheduleAt(date);
        else _shell.OpenScheduleAt(date, row.SubjectRaw, row.Time);
    }
    internal async Task OpenDeadlineAsync(WeekDeadline deadline)
    {
        if (!Days.Any(day => day.Date.Date == deadline.Due.Date && day.Day.Deadlines?.Any(row => row.Id == deadline.Id) == true) &&
            !(deadline.Due == DateTime.MinValue && UnknownDeadlines.Any(row => row.Id == deadline.Id))) return;
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var current = await RunAsync(() =>
        {
            var item = App.Homework.GetById(deadline.Id);
            return item is null ? null : new WeekDeadline(item.Id,
                (item.Status == "done" ? item.DueDateComputed : App.Homework.ComputeDueDate(
                    item.SubjectRawNormalized, item.CreatedAt, item.TargetNthOccurrence))?.Date ?? DateTime.MinValue,
                "", "", item.Status == "done");
        }, "week homework deadline");
        if (!operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId) return;
        if (current?.Due.Date != deadline.Due.Date) return;
        _shell.Section<HomeworkViewModel>(SectionKey.Homework).OpenPersonalTask(deadline.Id);
        _shell.NavigateTo(SectionKey.Homework);
    }
}

public sealed partial class WeekDayViewModel : ObservableObject
{
    private readonly WeekViewModel _owner;

    public WeekDayViewModel(WeekDay day, WeekViewModel owner, bool collapsed = false)
    {
        Day = day;
        _owner = owner;
        _isCollapsed = collapsed;
    }

    public WeekDay Day { get; }
    /// <summary>Position in the week (Mon = 0); drives the appear cascade.</summary>
    public int Index => Day.Dow - 1;
    public string Title => Day.Title;
    public string DateText => DayTitles.ShortDate(Day.Date, Loc.Current);
    public DateTime Date => Day.Date;
    public bool IsToday => Day.IsToday;
    public IReadOnlyList<WeekRow> Rows => Day.Rows;
    public IReadOnlyList<WeekDeadlineViewModel> Deadlines => (Day.Deadlines ?? [])
        .Select(deadline => new WeekDeadlineViewModel(deadline, _owner)).ToArray();
    public bool HasDeadlines => Day.Deadlines?.Count > 0;
    public IReadOnlyList<string> FreeIntervals => WeekFreeTime.Between(Day.Rows);
    public bool HasFreeIntervals => FreeIntervals.Count > 0;
    public bool ShowFreeIntervals => !IsCollapsed && HasFreeIntervals;
    public IReadOnlyList<WeekLessonViewModel> LessonRows => Day.Rows.Select(row => new WeekLessonViewModel(row, Date, _owner)).ToArray();
    public bool IsEmpty => Day.Rows.Count == 0;
    public string CountLabel => $"Пар: {Day.Rows.Count}";
    [ObservableProperty] private bool _isCollapsed;
    public string ToggleCaption => IsCollapsed ? "Показать пары" : "Свернуть пары";
    public string TimeSummary => Day.Rows.Count == 0 ? "" : $"{Day.Rows[0].Time}–{(Day.Rows[^1].TimeEnd.Length > 0 ? Day.Rows[^1].TimeEnd : Day.Rows[^1].Time)}";
    public bool CanCollapse => _owner.SearchQuery.Trim().Length == 0;
    partial void OnIsCollapsedChanged(bool value)
    { OnPropertyChanged(nameof(ToggleCaption)); OnPropertyChanged(nameof(ShowFreeIntervals)); }
    [RelayCommand] private void Toggle() => _owner.ToggleDay(Date);

    [RelayCommand] private void Open() => _owner.OpenDay(this);
}

public sealed partial class WeekDeadlineViewModel : ObservableObject
{
    private readonly WeekViewModel owner;
    public WeekDeadlineViewModel(WeekDeadline deadline, WeekViewModel owner)
    { Deadline = deadline; this.owner = owner; }
    public WeekDeadline Deadline { get; }
    public long Id => Deadline.Id;
    public string Label => $"{Deadline.Subject}: {Deadline.Text} · {(Deadline.Done ? "готово" : "активно")}";
    [RelayCommand] private Task Open() => owner.OpenDeadlineAsync(Deadline);
}

public sealed partial class WeekLessonViewModel : ObservableObject
{
    private readonly WeekViewModel owner;
    public WeekLessonViewModel(WeekRow row, DateTime date, WeekViewModel owner)
    { Row = row; Date = date; this.owner = owner; }
    public WeekRow Row { get; }
    public DateTime Date { get; }
    public string Time => Row.Time;
    public string Name => Row.Name;
    public string TypeLabel => Row.TypeLabel;
    public string Room => Row.Room;
    [RelayCommand] private void Open() => owner.OpenLesson(Date, Row);
}
