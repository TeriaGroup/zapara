using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vograph.Desktop;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Spec §7. The suite runs with motion OFF (TestAppBuilder, re-asserted by the UiTest ctor); these tests
/// switch it on for themselves and back off in a finally.</summary>
public class MotionTests : UiTest
{
    /// <summary>The marker resource Theme/Motion.axaml carries so App.SetMotion can find it among Application.Styles.</summary>
    private const string MotionStylesKey = "Motion.Styles";

    private static UiPrefs Prefs(bool animations) => new() { Animations = animations };

    /// <summary>Longer than every animation in this task (cascade: 7 × 40 ms delay + 240 ms).</summary>
    private static void Settle(int ms = 700)
    {
        for (var elapsed = 0; elapsed <= ms; elapsed += 20)
        {
            Thread.Sleep(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Ticks the headless render timer and yields until <paramref name="done"/> holds. Both halves matter:
    /// animated values are only sampled on a render tick, and an animation's completion continuation only runs
    /// once the UI thread is released — a blocking Thread.Sleep pump advances the values but never lets
    /// RunAsync's task (and with it Appear's finally) run. Awaiting returns to the dispatcher, which does.
    /// </summary>
    private static async Task SettleAsync(Func<bool> done, int timeoutMs = 3000)
    {
        for (var waited = 0; waited < timeoutMs && !done(); waited += 20)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private static bool Landed(Visual v, double opacity) => Math.Abs(v.Opacity - opacity) < 0.005;

    /// <summary>translateY(-1px) is the shape of the card hover lift (Theme/Typography.axaml) and of a button's
    /// :pressed scale (Theme/Controls/Buttons.axaml): a transform that lives at style priority.</summary>
    private static bool Lifted(Visual v) => v.RenderTransform is TransformOperations t && Math.Abs(t.Value.M32 + 1) < 0.001;

    private static Border Card(int index, double opacity = 1.0, string? cls = null)
    {
        var b = new Border { Height = 20, Opacity = opacity };
        if (cls is not null) b.Classes.Add(cls);
        Appear.SetKind(b, AppearKind.Cascade);
        Appear.SetIndex(b, index);
        return b;
    }

    private static Style LiftStyle() => new(x => x.OfType<Border>().Class("probe"))
    {
        Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("translateY(-1px)")) }
    };

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

    /// <summary>Was System_Setting_Is_Readable, an IsType-bool check on a bool-returning method: a tautology that
    /// also passed with the P/Invoke replaced by "return true". What matters about the system switch is how it
    /// composes — it can only ever veto the preference, never grant it — and that Refresh re-reads the
    /// preference. The reader itself is exercised as a smoke call: its value belongs to whichever machine runs
    /// the suite, so nothing is asserted about it.</summary>
    [Fact]
    public void System_Setting_Only_Ever_Vetoes_The_Preference()
    {
        Assert.False(new MotionSettings(Prefs(true), () => false).Enabled);  // preference on, system off
        Assert.False(new MotionSettings(Prefs(false), () => true).Enabled);  // preference off, system on
        Assert.True(new MotionSettings(Prefs(true), () => true).Enabled);    // both on

        var prefs = Prefs(false);
        var motion = new MotionSettings(prefs, () => true);
        Assert.False(motion.Enabled);
        Assert.Equal(TimeSpan.Zero, motion.Duration(180));
        prefs.Animations = true;
        motion.Refresh();
        Assert.True(motion.Enabled);
        Assert.Equal(TimeSpan.FromMilliseconds(180), motion.Duration(180));

        _ = MotionSettings.ReadSystemSetting(); // smoke: the user32 path must not throw where the suite runs
    }

    [AvaloniaFact]
    public void Resolve_Finds_The_Nearest_ViewModel_Or_Off()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var text = new TextBlock();
        var window = new Window { DataContext = shell, Content = new Border { Child = text } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
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
            // ON first, deliberately: the session already starts with the styles removed, so asserting the "go"
            // direction first would pass even if SetMotion(false) removed nothing at all.
            app.SetMotion(true);
            Pump();
            Assert.True(app.TryGetResource(MotionStylesKey, null, out var marker));
            Assert.Equal("on", marker);
            Assert.NotNull(button.Transitions);
            Assert.NotEmpty(button.Transitions!);

            app.SetMotion(false);
            Pump();
            Assert.False(app.TryGetResource(MotionStylesKey, null, out _));
            Assert.Null(button.Transitions);
        }
        finally { app.SetMotion(false); }
    }

    [AvaloniaFact]
    public async Task FadeSlide_Animates_With_A_Duration_And_Snaps_Without_One()
    {
        var from = new Border { Width = 50, Height = 50 };
        var to = new Border { Width = 50, Height = 50, IsVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Panel { Children = { from, to } } };
        window.Show();

        // What production actually builds the transition with, and the numbers it hands over.
        Assert.Null(Converters.PageTransition.Convert(false, typeof(IPageTransition), null, CultureInfo.InvariantCulture));
        var made = Assert.IsType<FadeSlide>(Converters.PageTransition.Convert(true, typeof(IPageTransition), null, CultureInfo.InvariantCulture));
        Assert.Equal(8, made.Offset);                                    // spec §7: the page slides 8 px
        Assert.Equal(TimeSpan.FromMilliseconds(180), made.Duration);

        // Duration zero is the instant branch: finished before Start even returns, and nothing left pinned on
        // RenderTransform, so a section shown after the switch went off is not drawn 8 px off-centre (T9-R4).
        var instant = new FadeSlide { Duration = TimeSpan.Zero }.Start(from, to, forward: true, CancellationToken.None);
        Assert.True(instant.IsCompleted);
        await instant;
        Assert.False(from.IsVisible);
        Assert.True(to.IsVisible);
        Assert.Null(from.GetValue(Visual.RenderTransformProperty));
        Assert.Null(to.GetValue(Visual.RenderTransformProperty));

        // With a duration there is a real animation: the task is still running when Start returns. No render tick
        // has happened yet, so this is not a timing race — it fails outright for a FadeSlide that snaps instead.
        from.IsVisible = true;
        var run = made.Start(to, from, forward: false, CancellationToken.None); // 180 ms
        Assert.False(run.IsCompleted);
        Settle(400);
        await run;
        Assert.False(to.IsVisible);
        Assert.True(from.IsVisible);
        Assert.Equal(1, from.Opacity, 3);
        Assert.Null(from.GetValue(Visual.RenderTransformProperty)); // the ±8 px translate is released (T9-R4)
        Assert.Null(to.GetValue(Visual.RenderTransformProperty));
    }

    /// <summary>Counts launched entrances instead of sampling a running animation: in some processes the headless
    /// render clock samples once and stops, and an opacity read mid-flight then says nothing about this code.</summary>
    [AvaloniaFact]
    public async Task Cascade_Runs_Once_Per_Row_Up_To_The_Cap_And_Only_Under_A_Fresh_Host()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        db.Services.Motion.Enabled = true; // direct: this test owns the switch
        var shell = new ShellViewModel(db.Services);
        try
        {
            app.SetMotion(true);
            var host = new StackPanel();
            Appear.SetCascadeHost(host, true);
            var window = new Window { Width = 300, Height = 400, DataContext = shell, Content = host };
            window.Show();                     // the host attaches here; its rows are realised right after
            Dispatcher.UIThread.RunJobs();

            Appear.CascadeRuns = 0;
            for (var i = 0; i < 8; i++) host.Children.Add(Card(i));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(8, Appear.CascadeRuns); // rows 0..7 each get their own staggered entrance

            // The 8-item cap, still inside the host's 400 ms window — so the cap and not the window is what
            // stopped these two.
            Appear.CascadeRuns = 0;
            host.Children.Add(Card(8));
            host.Children.Add(Card(57));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Appear.CascadeRuns);

            // Past that window even row 0 stays put: a reload of a list already on screen (a day change, a
            // homework toggle) must not replay the entrance.
            await SettleAsync(done: () => false, timeoutMs: 500);
            Appear.CascadeRuns = 0;
            host.Children.Add(Card(0));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Appear.CascadeRuns);
        }
        finally { app.SetMotion(false); }
    }

    [AvaloniaFact]
    public async Task Cascade_Ends_At_The_Styled_Opacity_And_Gives_The_Transform_Back()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        db.Services.Motion.Enabled = true;
        var shell = new ShellViewModel(db.Services);
        try
        {
            app.SetMotion(true);
            var host = new StackPanel();
            Appear.SetCascadeHost(host, true);
            var window = new Window { Width = 300, Height = 400, DataContext = shell, Content = host };
            window.Styles.Add(LiftStyle());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var first = Card(0);
            var past = Card(1, 0.6);                          // a past lesson card: the run ends at 0.6, not at 1
            var hovered = Card(2);                            // and this one is asked for a styled transform later
            var never = new Border { Height = 20 };           // the control: same style, never animated
            Appear.CascadeRuns = 0;
            foreach (var c in new Control[] { first, past, hovered, never }) host.Children.Add(c);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Appear.CascadeRuns);              // "never" carries no Appear.Kind

            await SettleAsync(() => Landed(first, 1.0) && Landed(past, 0.6) && Landed(hovered, 1.0)
                                    && first.GetValue(Visual.RenderTransformProperty) is null
                                    && past.GetValue(Visual.RenderTransformProperty) is null
                                    && hovered.GetValue(Visual.RenderTransformProperty) is null);

            Assert.Equal(1.0, first.Opacity, 2);
            Assert.Equal(0.6, past.Opacity, 2);
            // T9-R4: the animator's own transform is gone from local priority...
            Assert.Null(first.GetValue(Visual.RenderTransformProperty));
            Assert.Null(past.GetValue(Visual.RenderTransformProperty));

            // ...so a style that arrives afterwards wins, which is the whole point: Border.card has no transform
            // of its own and only gets one under the mouse (.hoverable:pointerover -> translateY(-1px)), exactly
            // the way this class does. Before the fix the animator's transform still sat at local priority and
            // shadowed it — a row that had cascaded showed M32 = 0 while a never-animated sibling lifted by -1,
            // i.e. turning animations ON took the hover feedback away.
            hovered.Classes.Add("probe");
            never.Classes.Add("probe");
            Pump();
            Assert.True(Lifted(never), "the un-animated control must show the styled lift");
            Assert.True(Lifted(hovered), "a row that ran Appear must not shadow the styled lift");
        }
        finally { app.SetMotion(false); }
    }

    [AvaloniaFact]
    public async Task Sidebar_Indicator_Follows_The_Active_Section()
    {
        var app = (App)Application.Current!;
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

        // The slide itself is a styled transition, so it exists exactly while Theme/Motion.axaml is included:
        // motion off (the whole suite) and the bar jumps to the item, motion on and it slides and stretches.
        // The positions above are asserted with it off on purpose — a running transition has no settled value.
        Assert.Null(indicator.Transitions);
        try
        {
            app.SetMotion(true);
            Pump();
            Assert.NotNull(indicator.Transitions);
            Assert.Contains(indicator.Transitions!, t => t is TransformOperationsTransition tr && tr.Property == Visual.RenderTransformProperty);
            Assert.Contains(indicator.Transitions!, t => t is DoubleTransition dt && dt.Property == Layoutable.HeightProperty);
        }
        finally { app.SetMotion(false); }
        Pump();
        Assert.Null(indicator.Transitions);
    }
}
