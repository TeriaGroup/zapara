using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
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

    public ScheduleViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        _composer = new ScheduleComposer(app);
        _segmentItems = BuildSegmentItems();
        _onLanguage = () => { SegmentItems = BuildSegmentItems(); _ = ReloadAsync(); };
        // Another group: run smart start again — the old offset was chosen for the old group, or for none.
        // Same guard as ReloadAsync: a section that never loaded has no stale offset to correct, and it
        // runs smart start on its own first load — starting Core work here would only outlive the shell.
        _onGroup = () => { if (_loaded) _ = InitializeAsync(); };
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
        App.Loc.LanguageChanged -= _onLanguage;
        _shell.GroupChanged -= _onGroup;
        _shell.ScheduleChanged -= _onSchedule;
        _shell.HomeworkChanged -= _onHomework;
    }

    /// <summary>Week/Teachers hand over a concrete date; the offset change reloads the day.</summary>
    public void ShowDate(DateTime date) => DayOffset = (date.Date - _clock().Date).Days;

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
        var version = ++_reloadVersion; // a reload already queued behind the gate must not overwrite the smart-start result
        var now = _clock();
        var model = await ComposeAsync(() => _composer.Compose(_composer.InitialOffset(now), now));
        _loaded = true;
        if (model is null || version != _reloadVersion) return;
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

    /// <summary>App.CoreGate (inside RunAsync) hands the gate to waiters in order, so awaiting a
    /// compose also awaits the composes queued ahead of it — including the ones a property change
    /// started and dropped.</summary>
    private Task<DayModel?> ComposeAsync(Func<DayModel> work) => RunAsync(work, "schedule");

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

    /// <summary>The card's own name travels with the map (renamed, type stripped), so the Maps header names the lesson.</summary>
    public void ShowMap(LessonRowViewModel row) => _shell.ShowMap(row.Row.Map, row.DisplayName);

    public async Task RenameAsync(LessonRowViewModel row)
    {
        var l = row.Row.Lesson;
        // RunAsync's T is a non-nullable class, and it already returns null for "no result": "no override" lands there too.
        var existing = await RunAsync<Override>(
            () => (App.Overrides.GetOverride(l.SubjectRaw, "global") ?? App.Overrides.GetOverride(l.SubjectRaw, $"weekday:{l.DayOfWeek}"))!,
            "rename");
        var dlg = new RenameDialogViewModel(ScheduleComposer.StripType(l.SubjectRaw, l.TypeRaw), l.SubjectRaw, l.DayOfWeek, existing); // shown stripped; persisted/keyed by the full SubjectRaw
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
        var l = row.Row.Lesson;
        var norm = ParityService.NormalizeSubject(l.SubjectRaw);
        var today = _clock().Date;
        var dues = await ComputeDuesAsync(norm, today);
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(row.DisplayName, nth => dues[Math.Clamp(nth, 1, 10) - 1]);
        if (!await _shell.Dialogs.ShowAsync(dlg)) return;
        if (!await RunAsync(() => App.Homework.AddHomework(l.SubjectRaw, dlg.Text.Trim(), dlg.Nth, createdAt: today), "homework add")) return;
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    public async Task EditHomeworkAsync(HomeworkItemViewModel hw)
    {
        var existing = await RunAsync<Homework>(() => App.Homework.GetById(hw.Id)!, "homework edit"); // null: gone, or the call failed
        if (existing is null) return;
        var dues = await ComputeDuesAsync(existing.SubjectRawNormalized, existing.CreatedAt);
        if (dues is null) return;
        var dlg = new HomeworkDialogViewModel(hw.Row.DisplayName, nth => dues[Math.Clamp(nth, 1, 10) - 1],
            existing.Text, existing.TargetNthOccurrence);
        if (!await _shell.Dialogs.ShowAsync(dlg)) return;
        if (!await RunAsync(() => App.Homework.UpdateHomework(hw.Id, dlg.Text.Trim(), dlg.Nth), "homework edit")) return;
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    /// <summary>Every due date the stepper can show, computed once off the UI thread: the dialog then
    /// only indexes the table, so changing N never touches SQLite from the UI thread.</summary>
    private Task<DateTime?[]?> ComputeDuesAsync(string subjectNormalized, DateTime from) =>
        RunAsync(() => Enumerable.Range(1, 10).Select(n => App.Homework.ComputeDueDate(subjectNormalized, from, n)).ToArray(), "homework");

    public async Task ToggleDoneAsync(HomeworkItemViewModel hw)
    {
        if (!await RunAsync(() => App.Homework.MarkDone(hw.Id, !hw.IsDone), "homework done")) return;
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    public async Task DeleteHomeworkAsync(HomeworkItemViewModel hw)
    {
        var confirm = new ConfirmDialogViewModel(T("hwDelete"), T("hwDeleteConfirm", hw.Text), T("delete"), danger: true);
        if (!await _shell.Dialogs.ShowAsync(confirm)) return;
        if (!await RunAsync(() => App.Homework.Delete(hw.Id), "homework delete")) return;
        await ReloadAsync();
        await RaiseHomeworkAsync();
    }

    /// <summary>Tells the Homework section and the sidebar badge about a card-side mutation; the flag keeps
    /// our own _onHomework handler from starting a second compose of the day we just reloaded. Awaited by
    /// every caller so the badge refresh's Core read finishes before the caller's own Task completes, instead
    /// of racing a test's teardown Dispose of the SQLite connection it reads from.</summary>
    private async Task RaiseHomeworkAsync()
    {
        _raising = true;
        try { _shell.RaiseHomeworkChanged(); }
        finally { _raising = false; }
        await _shell.UpdateHomeworkBadgeAsync();
    }
}
