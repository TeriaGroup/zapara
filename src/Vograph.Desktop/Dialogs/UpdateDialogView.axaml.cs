using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Dialogs;

public partial class UpdateDialogView : UserControl
{
    public UpdateDialogView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ConfirmButton.Focus();
    }
}
