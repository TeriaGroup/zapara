using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>One modal dialog at a time, rendered as an overlay inside the main window. IsOpen is what Escape and the
/// hotkeys look at (it drops the instant the dialog completes); Current stays set through the close animation so the
/// card fades out with its content rather than empty. Showing a dialog over an open one replaces it: the one on
/// screen completes as cancelled and the host stays open for its replacement.</summary>
public sealed partial class DialogHostViewModel : ObservableObject
{
    public DialogHostViewModel(MotionSettings? motion = null) => Motion = motion ?? MotionSettings.Off;

    public MotionSettings Motion { get; }

    [ObservableProperty] private DialogViewModelBase? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private bool _isOpen;

    public bool HasDialog => IsOpen;

    public async Task<bool> ShowAsync(DialogViewModelBase dialog)
    {
        Current?.Cancel(); // one at a time: the dialog on screen completes as cancelled and hands the host over
        Current = dialog;
        IsOpen = true;
        var result = await dialog.Completion;
        // A dialog shown over this one already owns the host, so closing down from here would fade the newer one
        // out and strand it: IsOpen false gates Escape, Enter and Dismiss, and its own ShowAsync would never return.
        if (!ReferenceEquals(Current, dialog)) return result;
        IsOpen = false;
        var closing = Motion.Duration(120);
        if (closing > TimeSpan.Zero) await Task.Delay(closing);
        if (ReferenceEquals(Current, dialog) && !IsOpen) Current = null;
        return result;
    }

    /// <summary>Escape / backdrop click.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (IsOpen) Current?.Cancel();
    }

    /// <summary>Enter.</summary>
    [RelayCommand]
    private void ConfirmCurrent()
    {
        if (IsOpen && Current is { } d && d.ConfirmCommand.CanExecute(null)) d.ConfirmCommand.Execute(null);
    }
}
