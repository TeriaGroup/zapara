using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Summary;

public sealed record DayBar(string Label, int Count, double Height, DateTime? Date = null)
{
    public System.Windows.Input.ICommand? OpenCommand { get; init; }
}

public sealed partial class SummaryViewModel : ViewModelBase
{
    private const double BarMax = 40;
    private readonly ShellViewModel _shell;
    private readonly SummaryComposer _composer;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private int _version;
    private bool _initialized;
    private bool _suppress;

    public SummaryViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _composer = new SummaryComposer(app);
        _segmentItems = BuildSegmentItems();
        _reload = () => _ = ReloadAsync();
        app.Loc.LanguageChanged += _reload;
        shell.GroupChanged += _reload;
        shell.ScheduleChanged += _reload;
    }

    public override void Detach()
    {
        App.Loc.LanguageChanged -= _reload;
        _shell.GroupChanged -= _reload;
        _shell.ScheduleChanged -= _reload;
    }

    public override Task ActivateAsync() => ReloadAsync();

    public string Title => T("navSummary");

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _segmentIndex; // 0 odd, 1 even, 2 both
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _hasGroup;
    [ObservableProperty] private bool _hasCopy = true;
    [ObservableProperty] private string _loadError = "";
    public bool ShowNoGroup => IsLoaded && !HasGroup;
    public bool ShowNoCopy => IsLoaded && HasGroup && !HasCopy;
    public bool ShowSummary => HasGroup && HasCopy;
    partial void OnIsLoadedChanged(bool value) { OnPropertyChanged(nameof(ShowNoGroup)); OnPropertyChanged(nameof(ShowNoCopy)); }
    partial void OnHasGroupChanged(bool value) { OnPropertyChanged(nameof(ShowNoGroup)); OnPropertyChanged(nameof(ShowNoCopy)); OnPropertyChanged(nameof(ShowSummary)); }
    partial void OnHasCopyChanged(bool value) { OnPropertyChanged(nameof(ShowNoCopy)); OnPropertyChanged(nameof(ShowSummary)); }
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void OpenDay(DayBar? day) { if (day?.Date is { } date) _shell.OpenScheduleAt(date); }
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ChooseGroup() => _shell.NavigateTo(SectionKey.Settings);
    [CommunityToolkit.Mvvm.Input.RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RetrySummary()
    {
        await _shell.RefreshScheduleAsync(force: true, quiet: false);
        await ReloadAsync();
    }
    [ObservableProperty] private string _totalText = "—";
    [ObservableProperty] private IReadOnlyList<DayBar> _dayBars = Array.Empty<DayBar>();
    [ObservableProperty] private IReadOnlyList<CountItem> _types = Array.Empty<CountItem>();
    [ObservableProperty] private IReadOnlyList<CountItem> _subjects = Array.Empty<CountItem>();
    [ObservableProperty] private IReadOnlyList<CountItem> _teachers = Array.Empty<CountItem>();
    [ObservableProperty] private IReadOnlyList<CountItem> _rooms = Array.Empty<CountItem>();
    [ObservableProperty] private bool _showAllSubjects;
    [ObservableProperty] private bool _showAllTeachers;
    public IReadOnlyList<CountItem> VisibleSubjects => ShowAllSubjects ? Subjects : Subjects.Take(5).ToArray();
    public IReadOnlyList<CountItem> VisibleTeachers => ShowAllTeachers ? Teachers : Teachers.Take(5).ToArray();
    public bool CanExpandSubjects => Subjects.Count > 5;
    public bool CanExpandTeachers => Teachers.Count > 5;
    public string SubjectExpandCaption => ShowAllSubjects ? "Свернуть предметы" : $"Показать все предметы ({Subjects.Count})";
    public string TeacherExpandCaption => ShowAllTeachers ? "Свернуть преподавателей" : $"Показать всех преподавателей ({Teachers.Count})";
    partial void OnShowAllSubjectsChanged(bool value)
    { OnPropertyChanged(nameof(VisibleSubjects)); OnPropertyChanged(nameof(SubjectExpandCaption)); }
    partial void OnShowAllTeachersChanged(bool value)
    { OnPropertyChanged(nameof(VisibleTeachers)); OnPropertyChanged(nameof(TeacherExpandCaption)); }
    partial void OnSubjectsChanged(IReadOnlyList<CountItem> value)
    { OnPropertyChanged(nameof(VisibleSubjects)); OnPropertyChanged(nameof(CanExpandSubjects)); OnPropertyChanged(nameof(SubjectExpandCaption)); }
    partial void OnTeachersChanged(IReadOnlyList<CountItem> value)
    { OnPropertyChanged(nameof(VisibleTeachers)); OnPropertyChanged(nameof(CanExpandTeachers)); OnPropertyChanged(nameof(TeacherExpandCaption)); }
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ToggleSubjects() => ShowAllSubjects = !ShowAllSubjects;
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ToggleTeachers() => ShowAllTeachers = !ShowAllTeachers;
    [ObservableProperty] private int _sortIndex;
    public IReadOnlyList<string> SortOptions { get; } = ["По частоте", "По алфавиту"];
    private IReadOnlyList<CountItem> rawTypes = [];
    private IReadOnlyList<CountItem> rawSubjects = [];
    private IReadOnlyList<CountItem> rawTeachers = [];
    private IReadOnlyList<CountItem> rawRooms = [];
    partial void OnSortIndexChanged(int value) => ApplySort();
    private void ApplySort()
    {
        Types = SummaryBrowse.Sort(rawTypes, SortIndex).Select(row => row with { OpenCommand = OpenSummaryTypeCommand }).ToArray();
        Subjects = SummaryBrowse.Sort(rawSubjects, SortIndex).Select(row => row with { OpenCommand = OpenSummarySubjectCommand }).ToArray();
        Teachers = SummaryBrowse.Sort(rawTeachers, SortIndex).Select(row => row with { OpenCommand = OpenSummaryTeacherCommand }).ToArray();
        Rooms = SummaryBrowse.Sort(rawRooms, SortIndex).Select(row => row with { OpenCommand = OpenSummaryRoomCommand }).ToArray();
    }
    public bool HasRooms => Rooms.Count > 0;
    public bool HasTeachers => Teachers.Count > 0;

    private IList<string> BuildSegmentItems() => new[] { T("weekOdd"), T("weekEven"), T("summaryBothShort") };

    partial void OnSegmentIndexChanged(int value)
    {
        if (!_suppress) _ = ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        var today = _clock().Date;
        int? parity = _initialized ? SegmentIndex switch { 0 => 1, 1 => 2, _ => 0 } : null;
        var model = await RunAsync(() => _composer.Compose(parity, today), "summary");
        if (model is null)
        {
            if (version == _version && operation.IsCurrent) LoadError = "Сводку не удалось обновить. Последняя загруженная сводка сохранена.";
            return;
        }
        if (version != _version || !operation.IsCurrent) return;
        LoadError = "";
        _initialized = true;
        _suppress = true;
        SegmentIndex = model.Parity switch { 1 => 0, 2 => 1, _ => 2 };
        _suppress = false;
        Apply(model);
    }

    private void Apply(SummaryModel m)
    {
        HasGroup = m.HasGroup;
        HasCopy = m.HasCopy;
        IsLoaded = true;
        SegmentItems = BuildSegmentItems();
        TotalText = m.Total.ToString();
        var max = m.ByDay.Count == 0 ? 0 : m.ByDay.Max(d => d.Count);
        DayBars = m.ByDay.Select((d, index) => new DayBar(d.Name, d.Count, max == 0 ? 0 : Math.Round(BarMax * d.Count / max),
            index < (m.DayDates?.Count ?? 0) ? m.DayDates![index] : null)
            { OpenCommand = OpenDayCommand }).ToList();
        rawTypes = m.ByType;
        rawSubjects = m.Subjects;
        rawTeachers = m.Teachers;
        rawRooms = m.Rooms;
        rawEntries = m.Entries ?? [];
        ResetSummaryMatches();
        ApplySort();
        var scope = m.Parity switch { 1 => T("parityWeek", App.I18n.FormatParity(true)), 2 => T("parityWeek", App.I18n.FormatParity(false)), _ => T("summaryBoth") };
        Subtitle = $"{scope} · {App.Loc.Plural(m.Total, "lessons1", "lessons2", "lessons5")}";
        OnPropertyChanged(nameof(HasRooms));
        OnPropertyChanged(nameof(HasTeachers));
        OnPropertyChanged(nameof(Title));
    }
}
