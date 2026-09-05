using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Dialogs;

public partial class GroupPickerDialogView : UserControl
{
    public GroupPickerDialogView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is GroupPickerDialogViewModel vm && vm.ConfirmCommand.CanExecute(null)) vm.ConfirmCommand.Execute(null);
    }
}
