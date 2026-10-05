using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ControlsTests : UiTest
{
    private static Window Gallery()
    {
        var icon = (Geometry)Application.Current!.FindResource("Icon.Calendar")!;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24), Width = 520 };
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new Button { Content = "Сохранить", Classes = { "primary" } },
                new Button { Content = "Отмена" },
                new Button { Content = "Призрак", Classes = { "ghost" } },
                new Button { Content = "Удалить", Classes = { "danger" } },
                new Button { Content = new Icon { Data = icon }, Classes = { "icon" } },
                new Button { Content = "К сегодня", Classes = { "pill" } },
            }
        });
        panel.Children.Add(new SegmentedControl { Items = new[] { "Вчера", "Сегодня", "Завтра" }, SelectedIndex = 2, Name = "Seg" });
        panel.Children.Add(new NavItem { Content = "Расписание", Icon = icon, IsActive = true, Badge = "2", Width = 232 });
        panel.Children.Add(new NavItem { Content = "Неделя", Icon = icon, Width = 232 });
        panel.Children.Add(new TextBox { PlaceholderText = "Номер группы…", Width = 260, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new Switch { Content = "Всегда все светофоры", IsChecked = true });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                new FriendDot { ColorIndex = 0, Fill = DotFill.Full },
                new FriendDot { ColorIndex = 1, Fill = DotFill.ThreeQuarters },
                new FriendDot { ColorIndex = 2, Fill = DotFill.Half },
                new FriendDot { ColorIndex = 3, Fill = DotFill.Ring },
                new FriendDot { ColorIndex = 4, Fill = DotFill.Off },
            }
        });
        panel.Children.Add(new EmptyState { Title = "Пар нет", Hint = "следующая пара — понедельник, 9:00", Icon = icon });
        return new Window { Width = 600, Height = 560, Content = panel };
    }

    [AvaloniaFact]
    public void Gallery_Renders_In_Both_Themes_Without_Binding_Errors()
    {
        var window = Gallery();
        window.Show();

        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "controls-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "controls-light");

        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public void SegmentedControl_Click_Changes_SelectedIndex_And_Moves_Thumb()
    {
        var seg = new SegmentedControl { Items = new[] { "Вчера", "Сегодня", "Завтра" }, SelectedIndex = 2 };
        var window = new Window { Width = 400, Height = 100, Content = new Border { Child = seg, Padding = new Thickness(20) } };
        window.Show();
        Pump();

        var buttons = seg.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(3, buttons.Count);
        Click(window, buttons[0]);

        Assert.Equal(0, seg.SelectedIndex);
        var thumb = seg.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Thumb");
        Assert.True(thumb.IsVisible);
        Assert.Equal(buttons[0].Bounds.Width, thumb.Width, precision: 1);
    }

    [Theory]
    [InlineData(100, DotFill.Full)]
    [InlineData(75, DotFill.ThreeQuarters)]
    [InlineData(50, DotFill.Half)]
    [InlineData(25, DotFill.Ring)]
    [InlineData(0, DotFill.Off)]
    [InlineData(-1, DotFill.Off)]
    public void FriendDot_Fill_From_Intersection_Score(int score, DotFill expected) =>
        Assert.Equal(expected, FriendDot.FromScore(score));

    [AvaloniaFact]
    public void NavItem_Active_PseudoClass_Follows_Property()
    {
        var item = new NavItem { Content = "X" };
        Assert.DoesNotContain(":active", item.Classes);
        item.IsActive = true;
        Assert.Contains(":active", item.Classes);
        item.IsCompact = true;
        Assert.Contains(":compact", item.Classes);
    }
}
