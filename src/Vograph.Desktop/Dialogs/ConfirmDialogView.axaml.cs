using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Dialogs;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ConfirmButton.Focus();
    }
}
