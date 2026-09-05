using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Core.Models;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

public sealed partial class GroupPickerDialogViewModel : DialogViewModelBase
{
    private readonly List<Group> _all;

    public GroupPickerDialogViewModel(IReadOnlyList<Group> groups, string? currentId)
    {
        _all = groups.OrderBy(g => g.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true)).ToList();
        Title = Loc.Current.T("groupPickTitle");
        ApplyFilter();
        Selected = _all.FirstOrDefault(g => g.Id == currentId);
    }

    public ObservableCollection<Group> Filtered { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private Group? _selected;

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(Group? value) => RefreshCanConfirm();

    protected override bool CanConfirm() => Selected is not null;

    private void ApplyFilter()
    {
        var keep = Selected;
        Filtered.Clear();
        foreach (var g in _all.Where(g => GroupSearch.Matches(g.Name, Query))) Filtered.Add(g);
        if (keep is not null && !Filtered.Contains(keep)) Selected = Filtered.Count == 1 ? Filtered[0] : null;
        else if (Selected is null && Filtered.Count == 1) Selected = Filtered[0];
    }
}
