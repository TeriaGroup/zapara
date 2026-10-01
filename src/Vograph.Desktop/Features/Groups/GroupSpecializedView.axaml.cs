using Avalonia.Controls;
using Avalonia.VisualTree;
namespace Vograph.Desktop.Features.Groups;
public partial class GroupSpecializedView : UserControl
{
    public GroupSpecializedView() => InitializeComponent();
    private void FocusFirstMissing(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SpaceFormRow { FirstMissingId: { } id }) return;
        var row = this.GetVisualDescendants().OfType<StackPanel>()
            .FirstOrDefault(panel => panel.DataContext is SpaceAnswerRow answer && answer.Id == id);
        var input = row is null ? null : FirstVisibleAnswerInput(row);
        input?.Focus();
    }
    internal static Control? FirstVisibleAnswerInput(StackPanel row) => row.GetVisualDescendants().OfType<Control>()
        .FirstOrDefault(control => control is TextBox or CheckBox && control.IsVisible && control.IsEnabled && control.Focusable);
}
