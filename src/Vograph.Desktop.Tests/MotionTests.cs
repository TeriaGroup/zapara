using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Vograph.Desktop;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Spec §7. The suite runs with motion OFF (UiTest ctor); these tests switch it on for themselves and back.</summary>
public class MotionTests : UiTest
{
    private static UiPrefs Prefs(bool animations) => new() { Animations = animations };

    /// <summary>Longer than every animation in this task (cascade: 7 × 40 ms delay + 240 ms).</summary>
    private static void Settle(int ms = 700) => Settle(() => false, ms);

    /// <summary>
    /// Pumps render ticks until <paramref name="done"/> holds, for at most <paramref name="ms"/>. Animated values are
    /// sampled on a render tick, and with the whole suite in one process ticks get dropped under load: a 240 ms
    /// animation was seen 40 ms in after 700 ms of forced ticks. Waiting for the end state rather than for a
    /// wall-clock budget keeps that out of the assertions.
    /// </summary>
    private static void Settle(Func<bool> done, int ms)
    {
        for (var elapsed = 0; elapsed <= ms && !done(); elapsed += 20)
        {
            Thread.Sleep(20);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static bool Landed(Visual v, double opacity) => Math.Abs(v.Opacity - opacity) < 0.005;

    private static TestDb Animated()
    {
        var db = TestDb.Create();
        db.Services.Motion.Refresh(); // prefs.Animations is true; TestDb's system switch says false → still off here
        return db;
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Enabled_Needs_Both_The_User_And_The_System(bool user, bool system, bool expected)
    {
        var m = new MotionSettings(Prefs(user), () => system);
        Assert.Equal(expected, m.Enabled);
        Assert.Equal(expected ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero, m.Duration(180));
    }

    [Fact]
    public void Refresh_Follows_The_Preference_And_Notifies()
    {
        var prefs = Prefs(true);
        var m = new MotionSettings(prefs, () => true);
        var changes = 0;
        m.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MotionSettings.Enabled)) changes++; };
        prefs.Animations = false;
        m.Refresh();
        Assert.False(m.Enabled);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void System_Setting_Is_Readable() => Assert.IsType<bool>(MotionSettings.ReadSystemSetting());

    [AvaloniaFact]
    public void Resolve_Finds_The_Nearest_ViewModel_Or_Off()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var text = new TextBlock();
        var window = new Window { DataContext = shell, Content = new Border { Child = text } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(db.Services.Motion, MotionSettings.Resolve(text)); // via the window's ShellViewModel
        Assert.Same(MotionSettings.Off, MotionSettings.Resolve(new TextBlock()));
        Assert.False(MotionSettings.Off.Enabled);
    }

    [AvaloniaFact]
    public void Motion_Styles_Come_And_Go_With_The_Switch()
    {
        var app = (App)Application.Current!;
        var window = new Window { Width = 200, Height = 100, Content = new Button { Content = "x" } };
        window.Show();
        var button = (Button)window.Content!;
        try
        {
            app.SetMotion(false);
            Assert.Null(button.Transitions);
            app.SetMotion(true);
            Pump();
            Assert.NotNull(button.Transitions);
            Assert.NotEmpty(button.Transitions!);
        }
        finally { app.SetMotion(false); }
    }

    [AvaloniaFact]
    public async Task FadeSlide_Hides_From_And_Shows_To()
    {
        var from = new Border { Width = 50, Height = 50 };
        var to = new Border { Width = 50, Height = 50, IsVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Panel { Children = { from, to } } };
        window.Show();

        var instant = new FadeSlide { Duration = TimeSpan.Zero };
        await instant.Start(from, to, forward: true, CancellationToken.None);
        Assert.False(from.IsVisible);
        Assert.True(to.IsVisible);

        from.IsVisible = true;
        var run = new FadeSlide().Start(to, from, forward: false, CancellationToken.None); // 180 ms
        Settle(400);
        await run;
        Assert.False(to.IsVisible);
        Assert.True(from.IsVisible);
        Assert.Equal(1, from.Opacity, 3);
    }

    [AvaloniaFact]
    public void Cascade_Runs_Only_Under_A_Freshly_Attached_Host_And_Ends_At_The_Styled_Opacity()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        db.Services.Motion.Enabled = true; // direct: this test owns the switch
        var shell = new ShellViewModel(db.Services);
        try
        {
            app.SetMotion(true);
            var items = new ItemsControl();
            Appear.SetCascadeHost(items, true);
            var cards = Enumerable.Range(0, 3).Select(i =>
            {
                var b = new Border { Height = 20, Opacity = i == 2 ? 0.6 : 1.0 };
                Appear.SetKind(b, AppearKind.Cascade);
                Appear.SetIndex(b, i);
                return b;
            }).ToList();
            items.ItemsSource = cards;
            var window = new Window { Width = 300, Height = 200, DataContext = shell, Content = items };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // animated values are sampled on a render tick
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(cards[1].Opacity < 1, "index 1 waits 40 ms at opacity 0");

            Settle(); // 2 × 40 + 240 ms and a margin — and past the 400 ms cascade window, for the late item below
            // ...then out of the animations themselves, which lag the wall clock whenever a render tick is dropped.
            Settle(() => Landed(cards[0], 1.0) && Landed(cards[1], 1.0) && Landed(cards[2], 0.6), 5000);
            Assert.Equal(1.0, cards[0].Opacity, 2);
            Assert.Equal(1.0, cards[1].Opacity, 2);
            Assert.Equal(0.6, cards[2].Opacity, 2); // the animation ends at the control's own opacity (a past lesson card)

            // Items added long after the host attached (a later reload) do not cascade.
            var late = new Border { Height = 20 };
            Appear.SetKind(late, AppearKind.Cascade);
            Appear.SetIndex(late, 0);
            items.ItemsSource = new[] { late };
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.0, late.Opacity, 3);
        }
        finally { app.SetMotion(false); }
    }

    [AvaloniaFact]
    public async Task Sidebar_Indicator_Follows_The_Active_Section()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();
        var sidebar = window.GetVisualDescendants().OfType<SidebarView>().Single();
        var indicator = sidebar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NavIndicator");
        var root = sidebar.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "Root");
        Assert.True(indicator.IsVisible);

        double IndicatorY() => ((TransformOperations)indicator.RenderTransform!).Value.M32;
        NavItem Item(string label) => sidebar.GetVisualDescendants().OfType<NavItem>().Single(n => n.Content as string == label);
        Assert.Equal(Item("Расписание").TranslatePoint(new Point(0, 9), root)!.Value.Y, IndicatorY(), 1);

        shell.NavigateTo(SectionKey.Week);
        Pump();
        Assert.Equal(Item("Неделя").TranslatePoint(new Point(0, 9), root)!.Value.Y, IndicatorY(), 1);
        Assert.Equal(Item("Неделя").Bounds.Height - 18, indicator.Height, 1);

        shell.NavigateTo(SectionKey.Settings); // the footer item, outside the two lists
        Pump();
        Assert.Equal(Item("Настройки").TranslatePoint(new Point(0, 9), root)!.Value.Y, IndicatorY(), 1);
        AssertNoBindingErrors();
    }
}
