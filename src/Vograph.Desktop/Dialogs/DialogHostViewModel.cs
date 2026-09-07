using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>One modal dialog at a time, rendered as an overlay inside the main window. IsOpen is what Escape and the
/// hotkeys look at (it drops the instant the dialog completes); Current stays set through the close animation so the
/// card fades out with its content rather than empty.</summary>
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
        Current?.Cancel();
        Current = dialog;
        IsOpen = true;
        var result = await dialog.Completion;
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
