using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#9 (G-2): карточка задания как на web — на ней одна отметка «Готово», прочие действия в листе «⋯»,
/// «Удалить» последнее и оформлено как опасное.</summary>
public class HomeworkCardSheetTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 14, 8, 0, 0);

    private static async Task<(TestDb Db, HomeworkViewModel Vm, MainWindow Window)> OpenAsync()
    {
        var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon7 };
        shell.Register(SectionKey.Homework, () => new HomeworkViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        shell.NavigateTo(SectionKey.Homework);
        var vm = (HomeworkViewModel)shell.Current!;
        await Waits.Until(() => vm.Groups.Count > 0, "homework loaded");
        Pump(); await Task.Delay(300); Pump();
        return (db, vm, window);
    }

    private static Control ById(Control root, string id) => root.GetVisualDescendants().OfType<Control>()
        .First(c => AutomationProperties.GetAutomationId(c) == id);

    private static Border FirstCard(Window window) => window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("hwrow"));

    [AvaloniaFact]
    public async Task Closed_card_shows_only_the_done_action_and_the_sheet_toggle()
    {
        var (db, _, window) = await OpenAsync();
        using var _ = db;
        foreach (var card in window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("hwrow") && b.IsEffectivelyVisible))
        {
            var row = (HomeworkRowViewModel)card.DataContext!;
            // Attachments stay on the card (they are content, as on web); everything else is the done mark and «⋯».
            var actions = card.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && b.Command != row.OpenFileCommand
                    && AutomationProperties.GetAutomationId(b) is not ("HomeworkRow.More" or "Homework.Done"))
                .Select(b => AutomationProperties.GetAutomationId(b) ?? b.Content as string)
                .ToList();
            Assert.Empty(actions);
            Assert.True(ById(card, "Homework.Done").IsEffectivelyVisible);
            Assert.True(ById(card, "HomeworkRow.More").IsEffectivelyVisible);
            Assert.False(ById(card, "HomeworkRow.Sheet").IsEffectivelyVisible);
        }
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task More_opens_the_sheet_with_every_action_and_delete_is_last_and_danger()
    {
        var (db, _, window) = await OpenAsync();
        using var _ = db;
        var card = FirstCard(window);
        var row = (HomeworkRowViewModel)card.DataContext!;
        Click(window, ById(card, "HomeworkRow.More"));
        Pump();
        Assert.True(row.IsSheetOpen);
        var sheet = ById(card, "HomeworkRow.Sheet");
        Assert.True(sheet.IsEffectivelyVisible);
        var buttons = sheet.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        var ids = buttons.Select(AutomationProperties.GetAutomationId).ToList();
        Assert.Equal("Homework.Edit", ids[0]);
        Assert.Contains(buttons, b => b.Content is "Копировать текст");
        Assert.Contains(buttons, b => b.Content is "Следующая пара");
        Assert.Equal("Homework.Delete", ids[^1]);
        Assert.Contains("danger", buttons[^1].Classes);

        Click(window, ById(card, "HomeworkRow.More"));
        Pump();
        Assert.False(row.IsSheetOpen);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Tapping_the_card_opens_and_closes_the_sheet_like_a_lesson_card()
    {
        var (db, _, window) = await OpenAsync();
        using var _ = db;
        var card = FirstCard(window);
        var row = (HomeworkRowViewModel)card.DataContext!;
        var text = card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("hwtext") && t.Text == row.Text);

        Click(window, text);
        Pump();
        Assert.True(row.IsSheetOpen, "нажатие по тексту задания открывает лист");
        Assert.True(ById(card, "HomeworkRow.Sheet").IsEffectivelyVisible);

        Click(window, card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("subject")));
        Pump();
        Assert.False(row.IsSheetOpen, "повторное нажатие по карточке закрывает лист");

        // Кнопки внутри карточки делают своё и лист не трогают.
        Click(window, ById(card, "Homework.Done"));
        Pump(); await Task.Delay(200); Pump();
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("hwrow")),
            b => b.DataContext is HomeworkRowViewModel { IsSheetOpen: true });
        AssertNoBindingErrors();
    }
}
