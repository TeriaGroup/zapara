using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Schedule;
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
    private bool _initialized;
    private bool _suppress;

    public WeekViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
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
    [ObservableProperty] private bool _hasGroup = true;

    partial void OnParityIndexChanged(int value)
    {
        if (!_suppress) _ = ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var version = ++_version;
        var today = _clock().Date;
        var parity = _initialized ? (ParityIndex == 0 ? 1 : 2) : 0;
        var model = await RunAsync(() => _composer.Compose(parity, today), "week");
        if (model is null || version != _version) return;
        _initialized = true;
        _suppress = true;
        ParityIndex = model.Parity == 1 ? 0 : 1;
        _suppress = false;
        Apply(model);
    }

    private void Apply(WeekModel m)
    {
        HasGroup = m.HasGroup;
        var suffix = T("weekCurrentSuffix");
        SegmentItems = new[] { T("weekOdd") + (m.IsOddToday ? suffix : ""), T("weekEven") + (m.IsOddToday ? "" : suffix) };
        Subtitle = $"{T("parityWeek", App.I18n.FormatParity(m.Parity == 1))} · {App.Loc.Plural(m.Total, "lessons1", "lessons2", "lessons5")}";
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
    public string Title => Day.Title;
    public string DateText => DayTitles.ShortDate(Day.Date, Loc.Current);
    public DateTime Date => Day.Date;
    public bool IsToday => Day.IsToday;
    public IReadOnlyList<WeekRow> Rows => Day.Rows;
    public bool IsEmpty => Day.Rows.Count == 0;

    [RelayCommand] private void Open() => _owner.OpenDay(this);
}
