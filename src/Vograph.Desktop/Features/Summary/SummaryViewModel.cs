using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Summary;

public sealed record DayBar(string Label, int Count, double Height);

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
    [ObservableProperty] private bool _hasGroup = true;
    [ObservableProperty] private string _totalText = "—";
    [ObservableProperty] private IReadOnlyList<DayBar> _dayBars = Array.Empty<DayBar>();
    [ObservableProperty] private IReadOnlyList<CountItem> _types = Array.Empty<CountItem>();
    [ObservableProperty] private IReadOnlyList<CountItem> _subjects = Array.Empty<CountItem>();
    [ObservableProperty] private IReadOnlyList<CountItem> _teachers = Array.Empty<CountItem>();
    [ObservableProperty] private IReadOnlyList<CountItem> _rooms = Array.Empty<CountItem>();
    public bool HasRooms => Rooms.Count > 0;
    public bool HasTeachers => Teachers.Count > 0;

    private IList<string> BuildSegmentItems() => new[] { T("weekOdd"), T("weekEven"), T("summaryBothShort") };

    partial void OnSegmentIndexChanged(int value)
    {
        if (!_suppress) _ = ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var version = ++_version;
        var today = _clock().Date;
        int? parity = _initialized ? SegmentIndex switch { 0 => 1, 1 => 2, _ => 0 } : null;
        var model = await RunAsync(() => _composer.Compose(parity, today), "summary");
        if (model is null || version != _version) return;
        _initialized = true;
        _suppress = true;
        SegmentIndex = model.Parity switch { 1 => 0, 2 => 1, _ => 2 };
        _suppress = false;
        Apply(model);
    }

    private void Apply(SummaryModel m)
    {
        HasGroup = m.HasGroup;
        SegmentItems = BuildSegmentItems();
        TotalText = m.Total.ToString();
        var max = m.ByDay.Count == 0 ? 0 : m.ByDay.Max(d => d.Count);
        DayBars = m.ByDay.Select(d => new DayBar(d.Name, d.Count, max == 0 ? 0 : Math.Round(BarMax * d.Count / max))).ToList();
        Types = m.ByType;
        Subjects = m.Subjects;
        Teachers = m.Teachers;
        Rooms = m.Rooms;
        var scope = m.Parity switch { 1 => T("parityWeek", App.I18n.FormatParity(true)), 2 => T("parityWeek", App.I18n.FormatParity(false)), _ => T("summaryBoth") };
        Subtitle = $"{scope} · {App.Loc.Plural(m.Total, "lessons1", "lessons2", "lessons5")}";
        OnPropertyChanged(nameof(HasRooms));
        OnPropertyChanged(nameof(HasTeachers));
        OnPropertyChanged(nameof(Title));
    }
}
