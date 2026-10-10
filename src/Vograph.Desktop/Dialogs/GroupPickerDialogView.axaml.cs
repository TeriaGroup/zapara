using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Linq;

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

    /// <summary>↓ from the search box goes into the list (selecting the first row when nothing is selected yet).
    /// ListBox itself is not focusable (standard Avalonia: only its item containers are), so the target is the
    /// selected row's realized container; List.Focus() alone would silently no-op.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not GroupPickerDialogViewModel vm) return;
        // #21: Enter выбирает единственную подходящую группу; само подтверждение — общий Enter диалога.
        if (e.Key == Key.Enter) { vm.PickSingleMatch(); return; }
        if (e.Key != Key.Down) return;
        if (vm.Selected is null && vm.Rows.FirstOrDefault(r => r.IsGroup) is { Group: { } first }) vm.Selected = first;
        if (List.ContainerFromIndex(List.SelectedIndex) is Control container) container.Focus();
        else List.Focus();
        e.Handled = true;
    }
}
