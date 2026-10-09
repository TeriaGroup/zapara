using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Models;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#21: общий каркас страниц — левый край, настройки, выбор группы, домашка, пустые состояния.</summary>
public class PageFrameTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 14, 8, 0, 0);

    private static async Task<(TestDb Db, ShellViewModel Shell, MainWindow Window)> Open(bool noGroup = false, double width = 1440, bool seed = true)
    {
        var db = TestDb.Create(seedPersonalization: seed);
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        if (noGroup) { var st = db.Services.Db.GetSettings(); st.MyGroupId = null; db.Services.Db.SaveSettings(st); }
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon7 };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Homework, () => new HomeworkViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Community, () => new CommunitiesViewModel(db.Services));
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = width, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        return (db, shell, window);
    }

    private static async Task Go(ShellViewModel shell, SectionKey key)
    {
        shell.NavigateTo(key);
        Pump(); await Task.Delay(300); Pump();
    }

    private static TextBlock PageTitle(Window window) => window.GetVisualDescendants().OfType<TextBlock>()
        .First(t => t.Classes.Contains("display") && t.IsEffectivelyVisible && t.Bounds.Width > 0);

    [AvaloniaFact]
    public async Task Page_titles_start_at_the_same_left_edge()
    {
        var (db, shell, window) = await Open();
        using var _ = db;
        var xs = new Dictionary<SectionKey, double>();
        foreach (var key in new[] { SectionKey.Schedule, SectionKey.Community, SectionKey.Chat, SectionKey.Settings, SectionKey.Homework })
        {
            await Go(shell, key);
            xs[key] = PageTitle(window).TranslatePoint(default, window)!.Value.X;
        }
        Assert.All(xs, kv => Assert.Equal(xs[SectionKey.Schedule], kv.Value, 0));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Open_settings_section_is_highlighted_and_back_is_hidden_when_list_is_visible()
    {
        var (db, shell, window) = await Open();
        using var _ = db;
        await Go(shell, SectionKey.Settings);
        var settings = (SettingsViewModel)shell.Current!;
        settings.OpenPanelCommand.Execute("study");
        Pump();
        Button Find(string id) => window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == id);
        Assert.Contains("active", Find("Settings.Category.Study").Classes);
        Assert.DoesNotContain("active", Find("Settings.Category.Appearance").Classes);
        Assert.True(Find("Settings.Category.Study").IsEffectivelyVisible);
        Assert.False(Find("Settings.Back").IsVisible);

        window.Width = 900; // список и детали не помещаются рядом — «Назад» снова нужна
        Pump(); await Task.Delay(200); Pump();
        Assert.True(Find("Settings.Back").IsEffectivelyVisible);
        Assert.False(Find("Settings.Category.Study").IsEffectivelyVisible);
        window.Close();
    }

    private static readonly Group[] Groups =
    [
        new() { Id = "1", Name = "И831Б" }, new() { Id = "2", Name = "И832Б" }, new() { Id = "3", Name = "А863С" },
        new() { Id = "4", Name = "Е452Б" }, new() { Id = "5", Name = "09С31" },
    ];

    [AvaloniaFact]
    public void Group_picker_has_recent_first_then_faculty_sections_and_enter_picks_single_match()
    {
        var vm = new GroupPickerDialogViewModel(Groups, null, recentIds: ["4", "missing", "1"]);
        Assert.Equal(new[] { "Недавние", "Е452Б", "И831Б", "Другие", "09С31", "Факультет А", "А863С", "Факультет И", "И832Б" },
            vm.Rows.Select(r => r.Header ?? r.Group!.Name));
        Assert.Null(vm.Selected); // без явного выбора кнопка по-прежнему неактивна

        vm.Query = "832";
        Assert.Equal(new[] { "Факультет И", "И832Б" }, vm.Rows.Select(r => r.Header ?? r.Group!.Name));
        vm.PickSingleMatch();
        Assert.Equal("И832Б", vm.Selected?.Name);
        Assert.Same(vm.Selected, vm.SelectedRow!.Group);

        var many = new GroupPickerDialogViewModel(Groups, null);
        many.Query = "И8";
        many.PickSingleMatch();
        Assert.Null(many.Selected); // две подходящие — Enter ничего не угадывает
        Assert.DoesNotContain(many.Rows, r => r.Header == "Недавние");
    }

    [AvaloniaFact]
    public async Task Group_picker_headers_are_not_selectable_and_placeholder_gives_example()
    {
        var vm = new GroupPickerDialogViewModel(Groups, "3", recentIds: ["3"]);
        var view = new GroupPickerDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 600, Height = 600 };
        window.Show(); Pump(); await Task.Delay(100); Pump();
        var items = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal(((GroupPickRow)item.DataContext!).IsGroup, item.IsEnabled));
        Assert.Equal("Например, И831Б", window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SearchBox").PlaceholderText);
        Assert.Equal("А863С", vm.Selected?.Name); // текущая группа предвыбрана
        window.Close();
        AssertNoBindingErrors();
    }

    [Fact]
    public void Recent_groups_are_unique_newest_first_and_bounded()
    {
        var prefs = new UiPrefs();
        foreach (var id in new[] { "1", "2", "3", "1", "4", "5", "6" }) prefs.RememberGroup(id, 5);
        Assert.Equal(new[] { "6", "5", "4", "1", "3" }, prefs.RecentGroupIds);
    }

    [AvaloniaFact]
    public async Task Homework_uses_full_width_counts_open_and_done_and_tucks_service_actions_into_more()
    {
        var (db, shell, window) = await Open();
        using var _ = db;
        await Go(shell, SectionKey.Homework);
        var vm = (HomeworkViewModel)shell.Current!;
        await Waits.Until(() => vm.Groups.Count > 0, "homework loaded");
        Pump();
        Assert.Matches(@"^Открыто: \d+ · Сдано: \d+$", vm.Counter);

        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.DoesNotContain(buttons, b => b.Content is "Сохранить сроки в календарь" or "Предпросмотр текущего списка" or "Выбрать несколько" or "Раскрыть все разделы");
        var more = buttons.Single(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Homework.More");
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.Equal(new[] { "Сохранить сроки в календарь", "Предпросмотр текущего списка", "Выбрать несколько", "Раскрыть все разделы", "Свернуть все разделы" },
            menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!));

        var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("hwrow"));
        var content = window.GetVisualDescendants().OfType<ContentControl>().First(c => c.Content is HomeworkViewModel);
        Assert.True(card.Bounds.Width > content.Bounds.Width - 100, $"card {card.Bounds.Width} of {content.Bounds.Width}");
        window.Close();
    }

    private static EmptyState VisibleEmpty(Window window) =>
        window.GetVisualDescendants().OfType<EmptyState>().Single(e => e.IsEffectivelyVisible);

    private static Button ActionOf(EmptyState empty) =>
        empty.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Action");

    [AvaloniaFact]
    public async Task No_group_empty_states_offer_choose_group_and_sidebar_card_invites_to_pick()
    {
        var (db, shell, window) = await Open(noGroup: true);
        using var _ = db;
        var expected = new (SectionKey Key, string Action)[]
        {
            // Сообщества и Чаты без входа — в #18 (PR #59), там своя логика кнопки входа.
            (SectionKey.Schedule, "Выбрать группу"), (SectionKey.Homework, "Выбрать группу"),
        };
        foreach (var (key, action) in expected)
        {
            await Go(shell, key);
            var button = ActionOf(VisibleEmpty(window));
            Assert.True(button.IsEffectivelyVisible, key.ToString());
            Assert.Equal(action, button.Content);
            Assert.Contains("primary", button.Classes);
            Assert.NotNull(button.Command);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && (t.Text ?? "").Contains("слева"));
        }
        // Без группы карточка в сайдбаре зовёт выбрать её, текст не обрезан.
        var cta = window.GetVisualDescendants().OfType<Grid>().Single(g => Avalonia.Automation.AutomationProperties.GetAutomationId(g) == "Shell.GroupCardCta");
        Assert.True(cta.IsEffectivelyVisible);
        Assert.Contains(cta.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Выберите группу" && t.TextWrapping == Avalonia.Media.TextWrapping.Wrap);
        window.Close();
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Empty_homework_list_offers_add_task()
    {
        var (db, shell, window) = await Open(seed: false);
        using var _ = db;
        await Go(shell, SectionKey.Homework);
        var vm = (HomeworkViewModel)shell.Current!;
        await Waits.Until(() => vm.ShowBrowseEmpty, "empty homework");
        Pump();
        var button = ActionOf(VisibleEmpty(window));
        Assert.Equal("Добавить задание", button.Content);
        Assert.Same(vm.AddCommand, button.Command);
        window.Close();
    }
}
