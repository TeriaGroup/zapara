using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#11: общие токены из design/tokens.json доступны в обеих темах, и базовые компоненты берут цвета из них.</summary>
public class DesignTokensTests
{
    public DesignTokensTests() => Loc.Init(new I18nService("ru"));

    private static JsonElement Tokens() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ResourceKeysTests.RepoRoot(), "design", "tokens.json"))).RootElement;

    private static string Pascal(string name) => string.Concat(name.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>"#RRGGBB" или "#RRGGBBAA" из tokens.json (CSS-порядок) → цвет Avalonia.</summary>
    private static Color Css(string hex)
    {
        var v = hex.TrimStart('#');
        return v.Length == 8 ? Color.Parse("#" + v[6..] + v[..6]) : Color.Parse("#" + v);
    }

    private static Color Brush(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryFindResource(key, variant, out var value), $"нет ресурса {key} ({variant})");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    [AvaloniaFact]
    public void Every_color_token_resolves_to_the_json_value_in_both_themes()
    {
        var tokens = Tokens();
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            foreach (var role in tokens.GetProperty("color").GetProperty(theme).EnumerateObject())
                Assert.Equal(Css(role.Value.GetString()!), Brush("Zp." + Pascal(role.Name), variant));
            foreach (var kind in tokens.GetProperty("lesson").GetProperty(theme).EnumerateObject())
                Assert.Equal(Css(kind.Value.GetString()!), Brush("Zp.Lesson." + Pascal(kind.Name), variant));
        }
        Assert.True(Application.Current!.TryFindResource("Zp.Font.Secondary", out var secondary));
        Assert.Equal(13d, secondary);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Base_components_use_tokens_and_render(string theme)
    {
        var variant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        Application.Current!.RequestedThemeVariant = variant;
        var buttons = new[] { ("primary", "Сохранить"), ("secondary", "Отмена"), ("ghost", "Подробнее"), ("danger filled", "Удалить") }
            .Select(b => { var button = new Button { Content = b.Item2 }; button.Classes.AddRange(b.Item1.Split(' ')); return button; }).ToList();
        var input = new TextBox { Text = "ivanov.a", Width = 260 };
        var card = new Border { Child = new TextBlock { Text = "Карточка" } };
        card.Classes.Add("card");
        var segmented = new SegmentedControl { Items = new[] { "День", "Неделя" }, SelectedIndex = 0 };
        var empty = new EmptyState { Title = "Заданий пока нет", Hint = "Добавьте задание сами или дождитесь задания от старосты." };
        var dialog = new ConfirmDialogView { DataContext = new ConfirmDialogViewModel("Удалить задание?", "Его нельзя будет восстановить.", "Удалить", true) };
        var badges = new WrapPanel();
        foreach (var (kind, label) in new[] { ("lecture", "Лекция"), ("practice", "Практика"), ("lab", "Лаба"), ("consult", "Консульт."), ("credit", "Зачёт"), ("exam", "Экзамен"), ("course", "Курсовая") })
            badges.Children.Add(LessonBadge(kind, label));
        var badge = (Border)badges.Children[1];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var b in buttons) row.Children.Add(b);
        var root = new StackPanel { Spacing = 16, Margin = new Thickness(24), Children = { row, input, segmented, badges, card, empty, dialog } };
        var window = new Window { Width = 760, Height = 640, Content = root };
        window.Show();
        Frames.Capture(window, $"design-tokens-{theme.ToLowerInvariant()}");

        Assert.Equal(Brush("Zp.Accent", variant), ((ISolidColorBrush)buttons[0].Background!).Color);
        Assert.Equal(Brush("Zp.OnAccent", variant), ((ISolidColorBrush)buttons[0].Foreground!).Color);
        Assert.Equal(Brush("Zp.Danger", variant), ((ISolidColorBrush)buttons[3].Background!).Color);
        Assert.Equal(Brush("Zp.OnDanger", variant), ((ISolidColorBrush)buttons[3].Foreground!).Color);
        Assert.Equal(Brush("Zp.BorderControl", variant), ((ISolidColorBrush)input.BorderBrush!).Color);
        Assert.Equal(Brush("Zp.Surface2", variant), ((ISolidColorBrush)card.Background!).Color);
        var dot = badge.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
        Assert.Equal(Brush("Zp.Lesson.Practice", variant), ((ISolidColorBrush)dot.Fill!).Color);
        window.Close();
    }

    private static Border LessonBadge(string kind, string label)
    {
        var wash = new Border();
        wash.Classes.Add("typewash");
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        dot.Classes.Add("typedot");
        var badge = new Border
        {
            Child = new Grid { Children = { wash, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 3), Children = { dot, new TextBlock { Text = label } } } } },
        };
        badge.Classes.AddRange(new[] { "chip", "type", "lessonbadge", kind });
        return badge;
    }
}
