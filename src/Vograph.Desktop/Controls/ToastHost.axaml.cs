using Avalonia.Controls;
using Avalonia.Input;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

public partial class ToastHost : UserControl
{
    public ToastHost() => InitializeComponent();

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if ((sender as Border)?.DataContext is ToastItem item) item.IsPaused = true;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if ((sender as Border)?.DataContext is ToastItem item) item.IsPaused = false;
    }
}
