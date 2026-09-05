using Avalonia.Controls;
using Avalonia.Input;

namespace Vograph.Desktop.Dialogs;

public partial class DialogHostView : UserControl
{
    public DialogHostView() => InitializeComponent();

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
