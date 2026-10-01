using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ManualEntry = subjects.Count == 0;
        Title = Loc.Current.T("hwPickSubject");
        ApplyFilter();
    }

    public ObservableCollection<SubjectOption> Filtered { get; } = new();
    public bool ManualEntry { get; }
    public bool ShowList => !ManualEntry;
    [ObservableProperty] private string manualSubject = "";
    partial void OnManualSubjectChanged(string value) => RefreshCanConfirm();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SubjectOption? _selected;
    public bool HasQuery => Query.Length > 0;
    public bool NoResults => !ManualEntry && Filtered.Count == 0;
    [RelayCommand] private void ClearQuery() => Query = "";

    partial void OnQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedChanged(SubjectOption? value) => RefreshCanConfirm();

    protected override bool CanConfirm() => Selected is not null || ManualEntry && ManualSubject.Trim().Length > 0;

    private void ApplyFilter()
    {
        var keep = Selected;
        Filtered.Clear();
        foreach (var s in _all.Where(s => Query.Trim().Length == 0 || s.Display.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase) || s.SubjectRaw.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)))
            Filtered.Add(s);
        if (keep is not null && !Filtered.Contains(keep)) Selected = Filtered.Count == 1 ? Filtered[0] : null;
        else if (Selected is null && Filtered.Count == 1) Selected = Filtered[0];
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(NoResults));
    }
}
