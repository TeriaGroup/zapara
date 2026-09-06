using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Dialogs;

public partial class SubjectPickerDialogView : UserControl
{
    public SubjectPickerDialogView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SearchBox.Focus();
    }
}
