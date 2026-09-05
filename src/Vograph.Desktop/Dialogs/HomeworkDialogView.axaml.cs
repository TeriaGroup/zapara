using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Dialogs;

public partial class HomeworkDialogView : UserControl
{
    public HomeworkDialogView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HwText.Focus();
    }
}
