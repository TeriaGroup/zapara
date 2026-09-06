using System.Collections.ObjectModel;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Teachers;

public sealed record TeacherItem(LecturerInfo Info, bool IsMine)
{
    public string Name => Info.Name;
    public string Kafedra => Info.Kafedra.Trim();
}

public sealed partial class TeachersViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _onGroup;
    private readonly Action _onLanguage;
    private TeacherIndex _index = new(Array.Empty<LecturerInfo>(), Array.Empty<LecturerLesson>());
    private HashSet<string> _myIds = new();
    private string _myGroupId = "";
    private string _myGroupName = "";
    private bool _invert;
    private bool _loadedOnce;

    public TeachersViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null, bool allowNetwork = true) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        AllowNetwork = allowNetwork;
        _onGroup = () => _ = LoadMyGroupAsync();
        _onLanguage = () => { OnPropertyChanged(nameof(Title)); Detail?.Relabel(); };
        shell.GroupChanged += _onGroup;
        app.Loc.LanguageChanged += _onLanguage;
    }

    public override void Detach()
    {
        _shell.GroupChanged -= _onGroup;
        App.Loc.LanguageChanged -= _onLanguage;
    }

    public override Task ActivateAsync() => _loadedOnce ? Task.CompletedTask : LoadAsync();

    public string Title => T("navTeachers");

    /// <summary>The network switch this instance was built with (AppServices.AllowNetwork at construction); tests assert it.</summary>
    public bool AllowNetwork { get; }
    public ObservableCollection<TeacherItem> Items { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _onlyMine = true;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private TeacherItem? _selected;
    [ObservableProperty] private TeacherDetailViewModel? _detail;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _loadError;
    public bool HasDetail => Detail is not null;

    partial void OnQueryChanged(string value) => ApplyFilter();
    partial void OnOnlyMineChanged(bool value) => ApplyFilter();
    partial void OnSelectedChanged(TeacherItem? value)
    {
        Detail = value is null ? null : new TeacherDetailViewModel(value.Info, _index.LessonsOf(value.Info.Id), value.IsMine, _myGroupId, _myGroupName, _invert, App.Loc, _clock().Date);
        OnPropertyChanged(nameof(HasDetail));
    }

    /// <summary>Local copy first (instant), my-teacher ids under the gate, then the network refresh behind the list.
    /// _loadedOnce is set only once a source of data was actually found, so a failed first load (no cache, no
    /// bundled copy, no network) is retried the next time the section is activated instead of sticking forever.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var have = App.Lecturers.IsLoaded || await Task.Run(() => App.Lecturers.LoadLocalAsync());
            if (!have && AllowNetwork) have = await Task.Run(() => App.Lecturers.RefreshAsync());
            if (!have)
            {
                LoadError = T("teachersLoadFail", T("teachersNoSource"));
                return;
            }
            _loadedOnce = true;
            await RebuildAsync();
            if (AllowNetwork) _ = RefreshInBackgroundAsync(); // RefreshAsync swallows its own failures, RunAsync the rest
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshInBackgroundAsync()
    {
        if (await Task.Run(() => App.Lecturers.RefreshAsync())) await RebuildAsync();
    }

    private async Task RebuildAsync()
    {
        _index = new TeacherIndex(App.Lecturers.Lecturers, App.Lecturers.Lessons);
        await LoadMyGroupAsync();
    }

    private sealed record MyGroupData(string Id, string Name, bool Invert, List<Lesson> Lessons);

    private async Task LoadMyGroupAsync()
    {
        var data = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            var id = s.MyGroupId ?? "";
            var name = id.Length == 0 ? "" : App.Db.GetGroup(id)?.Name ?? "";
            return new MyGroupData(id, name, s.ParityInvert, id.Length == 0 ? new List<Lesson>() : App.Db.GetAllLessonsForGroup(id));
        }, "teachers");
        if (data is null) return;
        _myGroupId = data.Id;
        _myGroupName = data.Name;
        _invert = data.Invert;
        _myIds = TeacherSearch.MyLecturerIds(data.Lessons, _index.Lecturers);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keep = Selected?.Info.Id;
        var filtered = _index.Filter(Query, OnlyMine, _myIds);
        Items.Clear();
        foreach (var l in filtered) Items.Add(new TeacherItem(l, _myIds.Contains(l.Id)));
        var total = _index.Lecturers.Count;
        CountText = filtered.Count < total ? T("teachersCount", filtered.Count, total) : total.ToString();
        Selected = keep is null ? null : Items.FirstOrDefault(i => i.Info.Id == keep);
    }
}

public sealed record TeacherRow(string Time, string TimeEnd, string Name, string TypeLabel, string Room, string Groups, string ParityLabel, bool IsMine);
public sealed record TeacherDay(string Title, bool IsToday, IReadOnlyList<TeacherRow> Rows);

/// <summary>Pure over the lecturer's lessons: no Core access, so it can be built on the UI thread when a row is selected.</summary>
public sealed partial class TeacherDetailViewModel : ObservableObject
{
    private static readonly string[] DayKeys = { "mon", "tue", "wed", "thu", "fri", "sat" };
    private readonly IReadOnlyList<LecturerLesson> _lessons;
    private readonly string _myGroupId;
    private readonly string _myGroupName;
    private readonly bool _invert;
    private readonly Loc _loc;
    private readonly DateTime _today;

    public TeacherDetailViewModel(LecturerInfo info, IReadOnlyList<LecturerLesson> lessons, bool isMine, string myGroupId, string myGroupName, bool invert, Loc loc, DateTime today)
    {
        Info = info;
        _lessons = lessons;
        IsMine = isMine;
        _myGroupId = myGroupId;
        _myGroupName = myGroupName;
        _invert = invert;
        _loc = loc;
        _today = today;
        _segmentItems = BuildSegments();
        _days = Build();
    }

    public LecturerInfo Info { get; }
    public string Name => Info.Name;
    public string Kafedra => Info.Kafedra.Trim();
    public bool HasKafedra => Kafedra.Length > 0;
    public bool IsMine { get; }
    public string MineLine => _loc.T(IsMine ? "teachersTeachesMine" : "teachersNotMine");

    [ObservableProperty] private IList<string> _segmentItems;
    [ObservableProperty] private int _parityIndex; // 0 both, 1 odd, 2 even
    [ObservableProperty] private IReadOnlyList<TeacherDay> _days;

    partial void OnParityIndexChanged(int value) => Days = Build();

    public void Relabel()
    {
        SegmentItems = BuildSegments();
        Days = Build();
        OnPropertyChanged(nameof(MineLine));
    }

    private IList<string> BuildSegments() => new[] { _loc.T("summaryBothShort"), _loc.T("oddShort"), _loc.T("evenShort") };

    private IReadOnlyList<TeacherDay> Build()
    {
        var loc = _loc;
        var todayDow = (int)_today.DayOfWeek;
        var days = new List<TeacherDay>(6);
        for (var dow = 1; dow <= 6; dow++)
        {
            var rows = _lessons
                .Where(l => l.DayOfWeek == dow)
                .Select(l => (Lesson: l, UserParity: _invert ? (l.Parity == 1 ? 2 : 1) : l.Parity))
                .Where(x => ParityIndex == 0 || x.UserParity == ParityIndex)
                .OrderBy(x => TimeSpan.TryParse(x.Lesson.TimeStart, out var t) ? t : TimeSpan.Zero)
                .ThenBy(x => x.UserParity)
                .Select(x => Row(x.Lesson, x.UserParity, loc))
                .ToList();
            days.Add(new TeacherDay(loc.T(DayKeys[dow - 1]), dow == todayDow, rows));
        }
        return days;
    }

    private TeacherRow Row(LecturerLesson l, int userParity, Loc loc)
    {
        var groups = l.Groups.Select(g => g.Number).Where(n => n.Length > 0).ToList();
        var groupsText = string.Join(", ", groups.Take(4)) + (groups.Count > 4 ? $" +{groups.Count - 4}" : "");
        var mine = l.Groups.Any(g => g.IdGroup == _myGroupId || (_myGroupName.Length > 0 && g.Number == _myGroupName));
        var room = string.IsNullOrWhiteSpace(l.ClassroomRaw) ? "—" : l.ClassroomRaw.Trim().TrimEnd(';').Replace("*", "").Trim();
        return new TeacherRow(l.TimeStart, l.TimeEnd, ScheduleComposer.StripType(l.DisciplineRaw, l.TypeRaw), DayTitles.TypeLabel(l.TypeRaw, loc),
            room, groupsText, loc.T(userParity == 1 ? "oddShort" : "evenShort"), mine);
    }
}

public static class TeacherConverters
{
    /// <summary>«Нет занятий» for an empty day: a compiled binding cannot negate an int, so Rows.Count goes through this.</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);
}
