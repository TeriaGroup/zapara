using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace Vograph.Desktop.Dialogs;

public partial class SubjectPickerDialogView : UserControl
{
    public SubjectPickerDialogView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || DataContext is not SubjectPickerDialogViewModel vm) return;
        if (vm.Selected is null && vm.Filtered.Count > 0) vm.Selected = vm.Filtered[0];
        if (List.ContainerFromIndex(List.SelectedIndex) is Control container) container.Focus();
        else List.Focus();
        e.Handled = true;
    }
}
