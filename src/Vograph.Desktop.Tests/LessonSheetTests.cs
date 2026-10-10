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
        // R2-07 / G-2: в листе — Карта · Домашка · Обсудить · «⋯»; редкие действия только в меню «⋯».
        Assert.Equal(new[] { Loc.Current.T("openMap"), "Домашка", Loc.Current.T("discussInGroupChat"), "⋯" }, labels);
        Assert.DoesNotContain("Переименовать", labels);
        Assert.DoesNotContain("Подробнее", labels);
        Assert.True(ById(card, "Lesson.Map").IsEffectivelyVisible);
        Assert.True(ById(card, "Lesson.Homework").IsEffectivelyVisible);
        var more = (Button)ById(card, "Lesson.SheetMore");
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.Equal(new[] { "Добавить задание", "Переименовать" }, menu.Items.OfType<MenuItem>().Select(i => i.Header as string));
        menu.ShowAt(more);
        Pump();
        Assert.Same(row.RenameCommand, menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "Lesson.Rename").Command);
        Assert.Same(row.AddHomeworkCommand, menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "Lesson.AddHomework").Command);
        menu.Hide();
        Pump();
        Assert.True(row.ShowDetails, "открытый лист показывает подробности (преподаватель, домашка)");

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

        // A button inside the sheet keeps its own click: «⋯» of the sheet opens its menu, not closes the sheet.
        Click(window, ById(card, "Lesson.SheetMore"));
        Pump();
        Assert.True(row.IsSheetOpen);
        ((Button)ById(card, "Lesson.SheetMore")).Flyout!.Hide();
        Pump();

        Click(window, ById(card, "Lesson.More"));
        Pump();
        Assert.False(row.IsSheetOpen);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Default_expansion_is_the_same_in_light_and_dark_and_on_a_past_day()
    {
        // R2-07: в ревью раскрытых карточек было разное число в светлой и тёмной теме. По умолчанию раскрыта
        // не больше одной (ближайшая пара дня), лист закрыт — независимо от темы и дня.
        var seen = new List<string>();
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            var (window, vm, db) = await OpenAsync();
            using var _ = db;
            SetTheme(theme);
            foreach (var day in new[] { Mon8.Date, Mon8.Date.AddDays(-7) })
            {
                vm.SelectDate(day);
                await Task.Delay(100); Pump();
                Assert.NotEmpty(vm.Lessons);
                Assert.True(vm.Lessons.Count(r => r.ShowDetails) <= 1, $"{theme} {day:dd.MM}: раскрыто {vm.Lessons.Count(r => r.ShowDetails)}");
                Assert.DoesNotContain(vm.Lessons, r => r.IsSheetOpen);
                seen.Add($"{day:dd.MM}:" + string.Join("", vm.Lessons.Select(r => r.ShowDetails ? "1" : "0")));
            }
            window.Close();
        }
        Assert.Equal(seen.Take(2), seen.Skip(2));
    }

    [AvaloniaFact]
    public async Task Opening_a_lesson_from_the_week_lands_on_its_sheet()
    {
        // G-2: в «Неделе» нажатие на пару ведёт в её день — и сразу открывает лист этой пары (на web — тоже лист).
        var (window, vm, db) = await OpenAsync();
        using var _ = db;
        var target = vm.Lessons.Last();
        var shell = (ShellViewModel)window.DataContext!;
        shell.OpenScheduleAt(Mon8.Date, target.Row.Lesson.SubjectRaw, target.TimeStart);
        await Waits.Until(() => vm.Lessons.Any(r => r.IsSheetOpen), "лист пары открыт");
        var open = Assert.Single(vm.Lessons, r => r.IsSheetOpen);
        Assert.Equal(target.Row.Lesson.SubjectRaw, open.Row.Lesson.SubjectRaw);
        Assert.Equal(target.TimeStart, open.TimeStart);
        window.Close();
    }
}
