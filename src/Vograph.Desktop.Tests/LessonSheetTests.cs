using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#9 (G-2): пара открывается в лист действий так же, как на web (LessonSheet): нажатие на карточку или «⋯»;
/// на закрытой карточке не больше одного действия.</summary>
public class LessonSheetTests : UiTest
{
    private static readonly DateTime Mon8 = new(2026, 9, 14, 8, 0, 0); // odd Monday: two pairs, the first is next

    private static async Task<(MainWindow Window, ScheduleViewModel Vm, IDisposable Db)> OpenAsync()
    {
        var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => Mon8);
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();
        return (window, vm, db);
    }

    private static List<Button> VisibleButtons(Control card) => card.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) != "Lesson.More").ToList();

    private static Control ById(Control root, string id) => root.GetVisualDescendants().OfType<Control>()
        .First(c => AutomationProperties.GetAutomationId(c) == id);

    [AvaloniaFact]
    public async Task Closed_cards_have_at_most_one_action_and_only_the_next_lesson_has_it()
    {
        var (window, vm, db) = await OpenAsync();
        using var _ = db;
        var cards = window.GetVisualDescendants().OfType<LessonCardView>().ToList();
        Assert.Equal(2, cards.Count);
        foreach (var card in cards)
        {
            var row = (LessonRowViewModel)card.DataContext!;
            Assert.False(row.IsSheetOpen);
            Assert.False(ById(card, "Lesson.Sheet").IsEffectivelyVisible);
            var inline = VisibleButtons(card).Where(b => b.Command == row.ShowMapCommand || b.Command == row.OpenHomeworksCommand
                || b.Command == row.DiscussCommand || b.Command == row.RenameCommand || b.Command == row.AddHomeworkCommand).ToList();
            Assert.True(inline.Count <= 1, $"{row.DisplayName}: {inline.Count} actions on a closed card");
            if (row.IsNext) Assert.Equal("Lesson.InlineMap", AutomationProperties.GetAutomationId(Assert.Single(inline)));
            else Assert.Empty(inline);
        }
        Assert.Single(vm.Lessons, row => row.IsNext);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Tapping_the_card_opens_the_sheet_with_the_web_actions_and_tapping_again_closes_it()
    {
        var (window, _, db) = await OpenAsync();
        using var __ = db;
        var card = window.GetVisualDescendants().OfType<LessonCardView>().First();
        var row = (LessonRowViewModel)card.DataContext!;

        Click(window, ById(card, "Lesson.Title"));
        Pump();
        Assert.True(row.IsSheetOpen);
        Assert.True(ById(card, "Lesson.Sheet").IsEffectivelyVisible);
        Assert.False(ById(card, "Lesson.InlineMap").IsEffectivelyVisible);
        var sheet = (Control)ById(card, "Lesson.Sheet");
        var labels = sheet.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
        Assert.Equal(Loc.Current.T("openMap"), labels[0]);
        Assert.Contains("Домашка", labels);
        Assert.Contains(Loc.Current.T("discussInGroupChat"), labels);
        Assert.Contains("Переименовать", labels);
        Assert.True(ById(card, "Lesson.Map").IsEffectivelyVisible);
        Assert.True(ById(card, "Lesson.Homework").IsEffectivelyVisible);
        Assert.True(ById(card, "Lesson.Rename").IsEffectivelyVisible);

        Click(window, ById(card, "Lesson.Title"));
        Pump();
        Assert.False(row.IsSheetOpen);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task The_more_button_toggles_the_sheet_and_buttons_inside_the_card_keep_their_own_click()
    {
        var (window, _, db) = await OpenAsync();
        using var __ = db;
        var card = window.GetVisualDescendants().OfType<LessonCardView>().First();
        var row = (LessonRowViewModel)card.DataContext!;

        Click(window, ById(card, "Lesson.More"));
        Pump();
        Assert.True(row.IsSheetOpen);

        // «Подробнее» inside the sheet toggles the details, not the sheet.
        var details = ((Control)ById(card, "Lesson.Sheet")).GetVisualDescendants().OfType<Button>().First(b => b.Command == row.ToggleDetailsCommand);
        var shown = row.ShowDetails;
        Click(window, details);
        Pump();
        Assert.NotEqual(shown, row.ShowDetails);
        Assert.True(row.IsSheetOpen);

        Click(window, ById(card, "Lesson.More"));
        Pump();
        Assert.False(row.IsSheetOpen);
        AssertNoBindingErrors();
    }
}
