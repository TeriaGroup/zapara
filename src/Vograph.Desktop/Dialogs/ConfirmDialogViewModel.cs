namespace Vograph.Desktop.Dialogs;

public sealed class ConfirmDialogViewModel : DialogViewModelBase
{
    public ConfirmDialogViewModel(string title, string message, string confirmText, bool danger)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        IsDanger = danger;
    }

    public string Message { get; }
    public string ConfirmText { get; }
    public bool IsDanger { get; }
}
