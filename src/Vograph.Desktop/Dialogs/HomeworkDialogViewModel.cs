using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>Create or edit homework: text + "in N lessons of this subject". The due date preview is
/// looked up on every N in a table the caller precomputed off the UI thread — no SQLite from here.</summary>
public sealed partial class HomeworkDialogViewModel : DialogViewModelBase
{
    private readonly Func<int, DateTime?> _computeDue;

    public HomeworkDialogViewModel(string subjectDisplay, Func<int, DateTime?> computeDue, string? existingText = null, int existingNth = 1)
    {
        _computeDue = computeDue;
        IsEdit = existingText is not null;
        Title = Loc.Current.T(IsEdit ? "hwEditTitle" : "hwTitle");
        SubjectLine = Loc.Current.T("hwSubject", subjectDisplay);
        _text = existingText ?? "";
        _nth = Math.Clamp(existingNth, 1, 10);
        UpdateDue();
    }

    public bool IsEdit { get; }
    public string SubjectLine { get; }

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private int _nth = 1;
    [ObservableProperty] private string _dueText = "";

    partial void OnTextChanged(string value) => RefreshCanConfirm();
    partial void OnNthChanged(int value) => UpdateDue();

    protected override bool CanConfirm() => !string.IsNullOrWhiteSpace(Text);

    [RelayCommand] private void Inc() => Nth = Math.Min(10, Nth + 1);
    [RelayCommand] private void Dec() => Nth = Math.Max(1, Nth - 1);

    private void UpdateDue()
    {
        var loc = Loc.Current;
        var due = _computeDue(Nth);
        DueText = due is null
            ? loc.T("hwNoDate")
            : loc.T("hwDue", $"{DayTitles.ShortDate(due.Value, loc)} ({loc.I18n.FormatDay(due.Value)})");
    }
}
