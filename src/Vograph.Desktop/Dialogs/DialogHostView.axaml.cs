using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Vograph.Desktop.Dialogs;

public partial class DialogHostView : UserControl
{
    private DialogHostViewModel? _vm;

    public DialogHostView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as DialogHostViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    /// <summary>
    /// When a dialog opens, move keyboard focus into the host so Enter/Escape reach OnKeyDown right away
    /// instead of waiting for the user to Tab or click into it. Deferred to the dispatcher so the dialog
    /// view's own OnLoaded focus (e.g. GroupPicker's search box, ConfirmDialogView's confirm button) runs
    /// first; the IsKeyboardFocusWithin check then leaves that focus alone instead of stealing it.
    /// </summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogHostViewModel.HasDialog) || _vm is not { HasDialog: true }) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!Root.IsKeyboardFocusWithin) Root.Focus();
        });
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DialogHostViewModel vm) vm.DismissCommand.Execute(null);
    }

    /// <summary>
    /// Enter-to-confirm as a bubbling KeyDown instead of a Window.KeyBinding: a KeyBinding gesture is matched
    /// even when a descendant (a multi-line TextBox inserting a newline) already set e.Handled, which would
    /// wrongly confirm the dialog while the user is still typing. A plain bubbling handler skips once handled.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not DialogHostViewModel vm) return;
        vm.ConfirmCurrentCommand.Execute(null);
        e.Handled = true;
    }
}
