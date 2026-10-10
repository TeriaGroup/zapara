using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#19: тулбары «Сегодня» и «Неделя» в одну строку, поля дат «дд.мм.гггг», шапка дня (D-03, D-04, D-05).</summary>
public class TodayWeekToolbarTests : UiTest
{
    private static readonly DateTime Mon14Morning = new(2026, 9, 14, 8, 0, 0);
    private static readonly DateTime Mon14Noon = new(2026, 9, 14, 12, 0, 0);
    private static readonly DateTime Wed9 = new(2026, 9, 9, 12, 0, 0);

    private static ScheduleViewModel Schedule(TestDb db, DateTime now)
    {
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => now);
        shell.Register(SectionKey.Schedule, () => vm);
        return vm;
    }

    private static Control? ById(Control root, string id) =>
        root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id);

    [AvaloniaFact]
    public async Task Today_header_has_one_meta_line_and_no_anchor_buttons()
    {
        using var db = TestDb.Create();
        var vm = Schedule(db, Mon14Morning);
        await vm.InitializeAsync();
        Assert.Equal("2 пары · 09:00–14:15 · перерывы 2 ч 5 мин", vm.DayMeta);
        Assert.DoesNotContain("2 пары", vm.DayLine);
        Assert.StartsWith("Сегодня · Понедельник, 14 сентября · нечётная неделя", vm.DayLine);

        var view = new ScheduleView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show(); SetTheme(ThemeVariant.Light); Pump();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.DoesNotContain(texts, t => t?.StartsWith("Занятия ") == true || t?.StartsWith("С 09:00 до") == true);
        var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.DoesNotContain(buttons, b => b.Content as string is "К ближайшей паре" or "К срокам домашки" or "Копировать день");
        // поиск дня — за кнопкой
        Assert.False(vm.ShowLessonSearch);
        vm.ToggleLessonSearchCommand.Execute(null);
        Assert.True(vm.ShowLessonSearch);
        vm.LessonSearch = "матан"; vm.ToggleLessonSearchCommand.Execute(null);
        Assert.Equal("", vm.LessonSearch); Assert.False(vm.ShowLessonSearch);
        // пустые сроки — одна строка
        Assert.Equal("Сроков на 3 дня нет", vm.DeadlineTitle);
        Assert.False(vm.HasDeadlineRows);
        // выбранный день в полосе дат — заливкой
        var selected = view.GetVisualDescendants().OfType<SelectionButton>().Single(b => b.IsChecked == true);
        Assert.True(selected.Classes.Contains("primary"));
        Assert.False(selected.Classes.Contains("secondary"));
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Current_or_next_lesson_card_sits_under_the_heading()
    {
        using var db = TestDb.Create();
        var morning = Schedule(db, Mon14Morning);
        await morning.InitializeAsync();
        Assert.Same(morning.Lessons[0], morning.HeroLesson);
        Assert.False(morning.HasHeroLesson); // первая пара и так первая в списке

        var noon = Schedule(db, Mon14Noon);
        await noon.InitializeAsync();
        Assert.True(noon.HasHeroLesson);
        Assert.Equal("12:40", noon.HeroLesson!.TimeStart);
        var view = new ScheduleView { DataContext = noon };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show(); Pump();
        var hero = Assert.IsType<Button>(ById(view, "Schedule.Hero"));
        Assert.True(hero.IsEffectivelyVisible);
        Assert.Contains(hero.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == noon.HeroLesson.DisplayName);
        Frames.Capture(window, "today-hero");
        hero.Command!.Execute(null);
        Assert.True(noon.HeroLesson.ShowDetails);
    }

    [AvaloniaFact]
    public async Task Date_fields_use_russian_format_and_a_calendar_button()
    {
        using var db = TestDb.Create();
        var vm = Schedule(db, Mon14Morning);
        await vm.InitializeAsync();
        var view = new ScheduleView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show(); Pump();
        var picker = view.GetVisualDescendants().OfType<CalendarDatePicker>().Single();
        Assert.Equal("дд.мм.гггг", picker.PlaceholderText);
        Assert.Equal(CalendarDatePickerFormat.Custom, picker.SelectedDateFormat);
        Assert.Equal(DayOfWeek.Monday, picker.FirstDayOfWeek);
        var box = picker.GetVisualDescendants().OfType<TextBox>().First();
        Assert.Equal("14.09.2026", box.Text);
        var button = picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Button");
        Assert.Equal("Открыть календарь", AutomationProperties.GetName(button));
        Assert.Single(button.GetVisualDescendants().OfType<Icon>());
        Assert.Empty(button.GetVisualDescendants().OfType<TextBlock>()); // без цифры дня, похожей на счётчик
    }

    [AvaloniaFact]
    public async Task Week_toolbar_is_one_row_and_tools_live_in_the_more_menu()
    {
        using var db = TestDb.Create();
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services), () => Wed9);
        await vm.ReloadAsync();
        Assert.Equal("7–13 сент. · чётная", vm.WeekHeading);
        var view = new WeekView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show(); SetTheme(ThemeVariant.Light); Pump();

        var toolbar = view.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "WeekToolbar");
        var ids = new[] { "Week.Prev", "Week.Title", "Week.Next", "Week.Today", "Week.Search", "Week.More" };
        var tops = ids.Select(id => ById(view, id)!).Select(c => c.TranslatePoint(new Avalonia.Point(0, c.Bounds.Height / 2), toolbar)!.Value.Y).ToList();
        Assert.All(tops, y => Assert.InRange(y, tops[0] - 2, tops[0] + 2)); // одна строка
        Assert.True(toolbar.Bounds.Height < 60);

        // по умолчанию между тулбаром и неделей ничего нет
        Assert.Empty(view.GetVisualDescendants().OfType<CalendarDatePicker>().Where(p => p.IsEffectivelyVisible));
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Ближайшие зачёты и экзамены");
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Показано дней") == true);

        var more = Assert.IsType<Button>(ById(view, "Week.More"));
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(new[] { "Копировать неделю", "Сохранить неделю в календарь (.ics)", "Сравнить с другой неделей…", "Первый день с парами", "Только дни с парами", "Зачёты и экзамены на 28 дней…" }, headers);

        vm.OpenComparePickerCommand.Execute(null); Pump();
        Assert.Single(view.GetVisualDescendants().OfType<CalendarDatePicker>().Where(p => p.IsEffectivelyVisible));
        vm.OpenAssessmentPickerCommand.Execute(null); Pump();
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Ближайшие зачёты и экзамены");
        vm.ToggleSearchCommand.Execute(null); Pump();
        Assert.True(vm.ShowSearch);
        vm.ToggleOnlyDaysWithClassesCommand.Execute(null);
        Assert.True(vm.OnlyDaysWithClasses);
        Pump();
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Показано дней") == true);
        AssertNoBindingErrors();
    }
}
