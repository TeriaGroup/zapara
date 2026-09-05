using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Dialogs;

/// <summary>One modal dialog at a time, rendered as an overlay inside the main window (headless-testable, no extra windows).</summary>
public sealed partial class DialogHostViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private DialogViewModelBase? _current;

    public bool HasDialog => Current is not null;

    public async Task<bool> ShowAsync(DialogViewModelBase dialog)
    {
        Current?.Cancel();
        Current = dialog;
        dialog.Closed += d => { if (ReferenceEquals(Current, d)) Current = null; };
        return await dialog.Completion;
    }

    /// <summary>Escape / backdrop click.</summary>
    [RelayCommand]
    private void Dismiss() => Current?.Cancel();

    /// <summary>Enter.</summary>
    [RelayCommand]
    private void ConfirmCurrent()
    {
        if (Current is { } d && d.ConfirmCommand.CanExecute(null)) d.ConfirmCommand.Execute(null);
    }
}
