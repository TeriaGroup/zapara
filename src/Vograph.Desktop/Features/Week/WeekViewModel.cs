using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Week;

public sealed partial class WeekViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly WeekComposer _composer;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private int _version;
    private bool _suppress;
    private DateTime _selectedDate;

    public WeekViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _selectedDate = _clock().Date;
        _composer = new WeekComposer(app);
        _segmentItems = new[] { T("weekOdd"), T("weekEven") };
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

    public string Title => T("navWeek");
    public ObservableCollection<WeekDayViewModel> Days { get; } = new();

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _parityIndex; // 0 odd, 1 even; lands on the current week on the first load
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _hasGroup;
    [ObservableProperty] private bool _hasCopy;
    [ObservableProperty] private string _weekRange = "";
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
        _selectedDate = _selectedDate.AddDays(7);
        _ = ReloadAsync();
    }

    [RelayCommand] private Task PreviousWeek() { _selectedDate = _selectedDate.AddDays(-7); return ReloadAsync(); }
    [RelayCommand] private Task NextWeek() { _selectedDate = _selectedDate.AddDays(7); return ReloadAsync(); }
    [RelayCommand] private Task CurrentWeek() { _selectedDate = _clock().Date; return ReloadAsync(); }
    [RelayCommand] private void ChooseGroup() => _shell.NavigateTo(SectionKey.Settings);
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
        var today = _clock().Date;
        var selected = _selectedDate;
        var model = await RunAsync(() => _composer.ComposeCalendar(selected, today), "week");
        if (model is null)
        {
            if (version == _version && operation.IsCurrent) LoadError = "Неделю не удалось открыть. Последняя загруженная неделя сохранена.";
            return;
        }
        if (version != _version || !operation.IsCurrent || selected != _selectedDate) return;
        LoadError = "";
        _suppress = true;
        ParityIndex = model.Parity == 1 ? 0 : 1;
        _suppress = false;
        Apply(model);
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
        WeekRange = $"{monday.ToString("d MMMM", CultureInfo.GetCultureInfo("ru-RU"))} — {monday.AddDays(6).ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"))}";
        Days.Clear();
        foreach (var d in m.Days) Days.Add(new WeekDayViewModel(d, this));
        OnPropertyChanged(nameof(Title));
    }

    public void OpenDay(WeekDayViewModel day) => _shell.OpenScheduleAt(day.Date);
}

public sealed partial class WeekDayViewModel : ObservableObject
{
    private readonly WeekViewModel _owner;

    public WeekDayViewModel(WeekDay day, WeekViewModel owner)
    {
        Day = day;
        _owner = owner;
    }

    public WeekDay Day { get; }
    /// <summary>Position in the week (Mon = 0); drives the appear cascade.</summary>
    public int Index => Day.Dow - 1;
    public string Title => Day.Title;
    public string DateText => DayTitles.ShortDate(Day.Date, Loc.Current);
    public DateTime Date => Day.Date;
    public bool IsToday => Day.IsToday;
    public IReadOnlyList<WeekRow> Rows => Day.Rows;
    public bool IsEmpty => Day.Rows.Count == 0;
    public string CountLabel => $"Пар: {Day.Rows.Count}";

    [RelayCommand] private void Open() => _owner.OpenDay(this);
}
