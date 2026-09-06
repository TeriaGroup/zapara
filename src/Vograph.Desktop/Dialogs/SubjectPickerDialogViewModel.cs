using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>Step 1 of «＋ Добавить» in the Homework section: which subject of my group.</summary>
public sealed partial class SubjectPickerDialogViewModel : DialogViewModelBase
{
    private readonly IReadOnlyList<SubjectOption> _all;

    public SubjectPickerDialogViewModel(IReadOnlyList<SubjectOption> subjects)
    {
        _all = subjects;
        Title = Loc.Current.T("hwPickSubject");
        ApplyFilter();
    }

    public ObservableCollection<SubjectOption> Filtered { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SubjectOption? _selected;

    partial void OnQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedChanged(SubjectOption? value) => RefreshCanConfirm();

    protected override bool CanConfirm() => Selected is not null;

    private void ApplyFilter()
    {
        var keep = Selected;
        Filtered.Clear();
        foreach (var s in _all.Where(s => Query.Trim().Length == 0 || s.Display.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase) || s.SubjectRaw.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)))
            Filtered.Add(s);
        if (keep is not null && !Filtered.Contains(keep)) Selected = Filtered.Count == 1 ? Filtered[0] : null;
        else if (Selected is null && Filtered.Count == 1) Selected = Filtered[0];
    }
}
