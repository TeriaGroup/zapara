using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Core.Models;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>#21: строка списка выбора группы — либо заголовок раздела («Недавние», «Факультет И»), либо группа.</summary>
public sealed record GroupPickRow(string? Header, Group? Group)
{
    public bool IsHeader => Header is not null;
    public bool IsGroup => Group is not null;
    public string Name => Group?.Name ?? "";
}

public sealed partial class GroupPickerDialogViewModel : DialogViewModelBase
{
    private readonly List<Group> _all;

    private readonly List<Group> _recent;

    /// <param name="recentIds">Недавно выбранные группы, новые первыми (UiPrefs.RecentGroupIds); показываются над каталогом.</param>
    public GroupPickerDialogViewModel(IReadOnlyList<Group> groups, string? currentId, bool allowManual = false, IReadOnlyList<string>? recentIds = null)
    {
        AllowManual = allowManual && groups.Count == 0;
        _all = groups.OrderBy(g => g.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true)).ToList();
        Title = Loc.Current.T("groupPickTitle");
        var byId = _all.GroupBy(g => g.Id).ToDictionary(x => x.Key, x => x.First());
        _recent = (recentIds ?? []).Distinct().Select(id => byId.GetValueOrDefault(id)).OfType<Group>().Take(RecentLimit).ToList();
        ApplyFilter();
        Selected = _all.FirstOrDefault(g => g.Id == currentId);
    }

    public const int RecentLimit = 5;

    public ObservableCollection<Group> Filtered { get; } = new();

    /// <summary>То, что видно в списке: «Недавние» сверху, затем группы по факультету (первая буква номера).
    /// Filtered остаётся плоским списком подходящих групп без повторов.</summary>
    public ObservableCollection<GroupPickRow> Rows { get; } = new();

    /// <summary>Выбранная строка списка; заголовок выбрать нельзя, строка группы ставит Selected.</summary>
    public GroupPickRow? SelectedRow
    {
        get => Selected is null ? null : Rows.FirstOrDefault(r => r.Group == Selected);
        set { if (value?.Group is { } g) Selected = g; }
    }

    /// <summary>Заголовок раздела каталога для группы: «Факультет И» по первой букве номера.</summary>
    public static string FacultyOf(Group g)
    {
        var letter = g.Name.Trim().FirstOrDefault();
        return char.IsLetter(letter) ? Loc.Current.T("groupFaculty", char.ToUpper(letter, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))) : Loc.Current.T("groupOther");
    }
    public bool AllowManual { get; }
    public bool ShowCatalog => !AllowManual;
    [ObservableProperty] private string manualName = "";
    partial void OnManualNameChanged(string value) => RefreshCanConfirm();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private Group? _selected;

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(Group? value) { RefreshCanConfirm(); OnPropertyChanged(nameof(SelectedRow)); }

    /// <summary>Enter в поиске: выбранная группа, а если ничего не выбрано и осталась ровно одна подходящая — она.</summary>
    public void PickSingleMatch()
    {
        if (Selected is null && Filtered.Count == 1) Selected = Filtered[0];
    }

    protected override bool CanConfirm() => Selected is not null || AllowManual && ManualName.Trim().Length > 0;

    private void ApplyFilter()
    {
        var keep = Selected;
        Filtered.Clear();
        foreach (var g in _all.Where(g => GroupSearch.Matches(g.Name, Query))) Filtered.Add(g);
        Rows.Clear();
        var recent = _recent.Where(Filtered.Contains).ToList();
        if (recent.Count > 0)
        {
            Rows.Add(new GroupPickRow(Loc.Current.T("groupRecent"), null));
            foreach (var g in recent) Rows.Add(new GroupPickRow(null, g));
        }
        foreach (var section in Filtered.Except(recent).GroupBy(FacultyOf))
        {
            Rows.Add(new GroupPickRow(section.Key, null));
            foreach (var g in section) Rows.Add(new GroupPickRow(null, g));
        }
        // Never auto-selects: narrowing to one match is not a pick. A selection the filter drops is cleared;
        // one that survives (or none at all) is left exactly as the user left it — Confirm (and the Enter key)
        // stays gated on CanConfirm until an explicit pick (a click, or ↓ into the list) sets Selected.
        if (keep is not null && !Filtered.Contains(keep)) Selected = null;
        OnPropertyChanged(nameof(SelectedRow));
    }
}
