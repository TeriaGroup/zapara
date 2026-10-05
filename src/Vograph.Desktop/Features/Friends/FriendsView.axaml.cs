using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Vograph.Desktop.Features.Friends;

public static class FriendConverters
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);
    public static readonly IValueConverter CurrentStroke = new FuncValueConverter<bool, double>(current => current ? 2 : 0);
    public static readonly IValueConverter IsNonEmpty = new FuncValueConverter<string?, bool>(value => !string.IsNullOrWhiteSpace(value));
}

public partial class FriendsView : UserControl
{
    public FriendsView() => InitializeComponent();
}
