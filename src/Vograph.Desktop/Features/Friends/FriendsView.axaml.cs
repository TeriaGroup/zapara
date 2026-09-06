using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Vograph.Desktop.Features.Friends;

public static class FriendConverters
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);
    public static readonly IValueConverter CurrentStroke = new FuncValueConverter<bool, double>(current => current ? 2 : 0);
}

public partial class FriendsView : UserControl
{
    public FriendsView() => InitializeComponent();

    private static void Commit(object? sender)
    {
        if (sender is TextBox { DataContext: FriendItemViewModel item }) item.CommitNamesCommand.Execute(null);
    }

    private void OnNamesLostFocus(object? sender, RoutedEventArgs e) => Commit(sender);

    private void OnNamesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Commit(sender);
        e.Handled = true;
    }
}
