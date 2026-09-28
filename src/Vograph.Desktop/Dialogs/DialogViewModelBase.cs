using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Dialogs;

/// <summary>A dialog is a view model shown by DialogHost; it completes with true (confirmed) or false (cancelled).</summary>
public abstract partial class DialogViewModelBase : ObservableObject
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty] private string _title = "";

    public Task<bool> Completion => _completion.Task;

    public event Action<DialogViewModelBase>? Closed;

    [RelayCommand]
    public virtual void Cancel() => Close(false);

    /// <summary>Lifecycle invalidation, distinct from a user asking to dismiss a changed draft.</summary>
    public virtual void Abort() => Close(false);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    public void Confirm()
    {
        if (!CanConfirm() || !Validate()) return;
        OnConfirm();
    }

    protected virtual bool CanConfirm() => true;

    protected virtual void OnConfirm() => Close(true);

    /// <summary>Last-moment check when the user presses Enter/Confirm; return false to keep the dialog open.</summary>
    protected virtual bool Validate() => true;

    protected void Close(bool result)
    {
        if (_completion.TrySetResult(result)) Closed?.Invoke(this);
    }

    protected void RefreshCanConfirm() => ConfirmCommand.NotifyCanExecuteChanged();
}
