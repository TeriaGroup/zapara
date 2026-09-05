using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

public sealed partial class RenameDialogViewModel : DialogViewModelBase
{
    public RenameDialogViewModel(string original, int dayOfWeek, Override? existing)
    {
        Original = original;
        DayOfWeek = dayOfWeek;
        Title = Loc.Current.T("renameTitle");
        ScopeItems = new[] { Loc.Current.T("global"), Loc.Current.T("weekdayOnly") };
        if (existing is not null)
        {
            HasExisting = true;
            _displayName = existing.DisplayName == original ? "" : existing.DisplayName;
            _note = existing.Note ?? "";
            _scopeIndex = existing.Scope == "global" ? 0 : 1;
        }
    }

    public string Original { get; }
    public int DayOfWeek { get; }
    public bool HasExisting { get; }
    public IList<string> ScopeItems { get; }
    public string OriginalLine => Loc.Current.T("original", Original);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _displayName = "";

    [ObservableProperty] private string _note = "";
    [ObservableProperty] private int _scopeIndex;

    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? Original : DisplayName.Trim();
    public string? EffectiveNote => string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
    public string Preview => Loc.Current.T("preview", EffectiveName);
    public string Scope => ScopeIndex == 0 ? "global" : $"weekday:{DayOfWeek}";

    /// <summary>"Сбросить": the caller removes the override(s) instead of saving.</summary>
    public bool ResetRequested { get; private set; }

    [RelayCommand]
    private void Reset()
    {
        ResetRequested = true;
        Close(true);
    }

    protected override bool Validate() => !string.IsNullOrWhiteSpace(DisplayName) || !string.IsNullOrWhiteSpace(Note);
}
