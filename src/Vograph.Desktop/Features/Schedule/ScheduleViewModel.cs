using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class ScheduleViewModel : ViewModelBase
{
    /// <summary>
    /// Core's Database owns one SqliteConnection and is not thread-safe, so two composes must not
    /// overlap. SemaphoreSlim hands the gate to waiters in order, so awaiting a reload also awaits
    /// the reloads queued ahead of it — including the ones a property change started and dropped.
    /// </summary>
    private readonly SemaphoreSlim _composeGate = new(1, 1);

    private readonly ScheduleComposer _composer;
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private int _reloadVersion;
    private bool _suppressReload;
    private bool _loaded;

    public ScheduleViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _composer = new ScheduleComposer(app);
        _segmentItems = BuildSegmentItems();
        app.Loc.LanguageChanged += () =>
        {
            SegmentItems = BuildSegmentItems();
            _ = ReloadAsync();
        };
        shell.GroupChanged += () => _ = ReloadAsync();
    }

    public ObservableCollection<LessonRowViewModel> Lessons { get; } = new();

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _dayOffset;
    [ObservableProperty] private int _segmentIndex = 1; // must agree with _dayOffset = 0, see SyncSegment
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string? _emptyTitle;
    [ObservableProperty] private string? _emptyHint;
    [ObservableProperty] private bool _showGoToday;

    public DateTime Date { get; private set; }

    private IList<string> BuildSegmentItems() => new[] { T("yesterday"), T("today"), T("tomorrow") };

    /// <summary>Smart start: today while lessons remain, otherwise tomorrow.</summary>
    public async Task InitializeAsync()
    {
        var now = _clock();
        var model = await ComposeAsync(() => _composer.Compose(_composer.InitialOffset(now), now));
        _loaded = true;
        if (model is null) return;
        _suppressReload = true;
        DayOffset = model.Offset;
        SyncSegment(model.Offset); // DayOffset may already hold that value, and then no change callback ran
        _suppressReload = false;
        Apply(model);
    }

    /// <summary>Recomposes the current day. A no-op before the first load: there is nothing to
    /// refresh yet, and a reload would show "today" instead of the smart-start day.</summary>
    public async Task ReloadAsync()
    {
        if (!_loaded) return;
        var version = ++_reloadVersion;
        var offset = DayOffset;
        var now = _clock();
        var model = await ComposeAsync(() => _composer.Compose(offset, now));
        if (model is null || version != _reloadVersion) return; // superseded by a newer reload
        Apply(model);
    }

    private async Task<DayModel?> ComposeAsync(Func<DayModel> work)
    {
        await _composeGate.WaitAsync();
        try
        {
            return await RunAsync(work, "schedule");
        }
        finally
        {
            _composeGate.Release();
        }
    }

    private void Apply(DayModel model)
    {
        Date = model.Date;
        Title = model.Title;
        Subtitle = model.Subtitle;
        Lessons.Clear();
        foreach (var row in model.Rows) Lessons.Add(new LessonRowViewModel(row, this));
        IsEmpty = model.Rows.Count == 0;
        EmptyTitle = model.EmptyTitle;
        EmptyHint = model.EmptyHint;
    }

    /// <summary>Segment thumb and the "go to today" pill are pure functions of the offset.</summary>
    private void SyncSegment(int offset)
    {
        SegmentIndex = offset is >= -1 and <= 1 ? offset + 1 : -1;
        ShowGoToday = offset != 0;
    }

    partial void OnDayOffsetChanged(int value)
    {
        SyncSegment(value);
        if (!_suppressReload) _ = ReloadAsync();
    }

    partial void OnSegmentIndexChanged(int value)
    {
        if (value is >= 0 and <= 2 && DayOffset != value - 1) DayOffset = value - 1;
    }

    [RelayCommand] private void PrevDay() => DayOffset--;
    [RelayCommand] private void NextDay() => DayOffset++;
    [RelayCommand] private void GoToday() => DayOffset = 0;

    public void ShowMap(LessonRowViewModel row) => _shell.ShowMap(row.Row.Map);
}
