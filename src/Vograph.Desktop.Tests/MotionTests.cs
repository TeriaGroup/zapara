using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
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

    /// <summary>
    /// A style whose selector reaches inside a template — «c|Switch /template/ Border#PART_Knob» — is NOT withdrawn
    /// when App.SetMotion removes Theme/Motion.axaml: Avalonia's detach never reaches the template children, so with
    /// «Анимации» off the knob went on gliding and the track on cross-fading for the life of the control. Every part's
    /// motion is owned by its control now (T10 R9), applied in code from MotionSettings. Both switches are flipped the
    /// way production flips them (App subscribes SetMotion to MotionSettings.PropertyChanged), and the third block is
    /// the one that used to fail: each part still had its transitions after motion went off.
    /// </summary>
    [AvaloniaFact]
    public void Template_Parts_Take_Their_Transitions_From_The_Motion_Switch()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        var motion = db.Services.Motion;
        var toggle = new Switch { Content = "Всегда все светофоры" };
        var seg = new SegmentedControl { Items = new[] { "Вчера", "Сегодня", "Завтра" }, SelectedIndex = 1 };
        var panel = new StackPanel { Children = { toggle, seg } };
        // MotionSettings.Resolve walks up to the nearest view model: without one every control is Off, on purpose.
        var window = new Window { Width = 320, Height = 140, DataContext = new Features.States.LoadingViewModel(db.Services), Content = panel };
        window.Show();
        Pump();
        var track = Part(toggle, "PART_Track");
        var knob = Part(toggle, "PART_Knob");
        var thumb = Part(seg, "PART_Thumb");
        try
        {
            Assert.False(motion.Enabled);
            Assert.Null(track.Transitions);
            Assert.Null(knob.Transitions);
            Assert.Null(thumb.Transitions);

            motion.Enabled = true;
            app.SetMotion(true);
            Pump();
            Assert.Contains(track.Transitions!, t => t is BrushTransition b && b.Property == Border.BackgroundProperty);
            Assert.Contains(knob.Transitions!, t => t is TransformOperationsTransition tr && tr.Property == Visual.RenderTransformProperty);
            Assert.Contains(thumb.Transitions!, t => t is TransformOperationsTransition tr && tr.Property == Visual.RenderTransformProperty);
            Assert.Contains(thumb.Transitions!, t => t is DoubleTransition d && d.Property == Layoutable.WidthProperty);

            motion.Enabled = false;
            app.SetMotion(false);
            Pump();
            Assert.Null(track.Transitions);
            Assert.Null(knob.Transitions);
            Assert.Null(thumb.Transitions);

            // Detached, the controls listen to nothing: a handler left on the app-wide MotionSettings would re-apply
            // the transitions here — and keep a closed window's controls alive with it.
            panel.Children.Clear();
            Pump();
            motion.Enabled = true;
            Pump();
            Assert.Null(track.Transitions);
            Assert.Null(knob.Transitions);
            Assert.Null(thumb.Transitions);
        }
        finally
        {
            motion.Enabled = false;
            app.SetMotion(false);
        }
    }

    /// <summary>
    /// A control attached BEFORE its view model arrives used to resolve MotionSettings.Off once — in
    /// OnAttachedToVisualTree, the only place that resolved — and then stay frozen for its whole life, while the
    /// «/template/» styles it replaced never depended on a DataContext at all. Three views here do hand a data
    /// context to an already-attached view (Dialogs/DialogHostView, Features/Maps/MapsView, MapFullscreenView), so
    /// motion is re-resolved on DataContextChanged as well as on attach (T10 R10a). The second half is what a
    /// re-resolve must not cost: no stacked transitions, no second sweep, and no handler left on the settings it
    /// used to follow.
    /// </summary>
    [AvaloniaFact]
    public async Task Motion_Is_Re_Resolved_When_The_Data_Context_Arrives()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        using var other = TestDb.Create(seedPersonalization: false); // a second shell, with MotionSettings of its own
        var motion = db.Services.Motion;
        var moved = other.Services.Motion;
        var toggle = new Switch { Content = "Всегда все светофоры" };
        var seg = new SegmentedControl { Items = new[] { "Вчера", "Сегодня", "Завтра" }, SelectedIndex = 1 };
        var skeleton = new Skeleton { Width = 200 };
        // No view model anywhere above the three: MotionSettings.Resolve walks to the root and returns Off.
        var window = new Window { Width = 320, Height = 220, Content = new StackPanel { Children = { toggle, seg, skeleton } } };
        window.Show();
        try
        {
            motion.Enabled = true;
            app.SetMotion(true);
            Pump();
            var track = Part(toggle, "PART_Track");
            var knob = Part(toggle, "PART_Knob");
            var thumb = Part(seg, "PART_Thumb");
            var shine = Part(skeleton, "PART_Shine");

            // Attached, laid out, motion on — and nothing to resolve: no transitions, and the shine keeps its park.
            Assert.Null(track.Transitions);
            Assert.Null(knob.Transitions);
            Assert.Null(thumb.Transitions);
            await SettleAsync(() => false, 200);
            Assert.Equal(-140, ShineAt(shine), 3);

            // The view model lands on the already-attached view, the way DialogHostView and MapsView get theirs.
            window.DataContext = new Features.States.LoadingViewModel(db.Services);
            Pump();
            Assert.NotNull(track.Transitions); // the finding: resolved once on attach, this stayed null for good
            Assert.NotNull(knob.Transitions);
            Assert.NotNull(thumb.Transitions);
            Assert.Contains(track.Transitions!, t => t is BrushTransition b && b.Property == Border.BackgroundProperty);
            Assert.Contains(knob.Transitions!, t => t is TransformOperationsTransition tr && tr.Property == Visual.RenderTransformProperty);
            Assert.Contains(thumb.Transitions!, t => t is TransformOperationsTransition tr && tr.Property == Visual.RenderTransformProperty);
            Assert.Contains(thumb.Transitions!, t => t is DoubleTransition d && d.Property == Layoutable.WidthProperty);
            await SettleAsync(() => ShineAt(shine) > -140);
            Assert.True(ShineAt(shine) > -140, $"the shine must sweep once the view model arrives; it sits at {ShineAt(shine)}");

            // Re-resolving is not additive: the same settings resolved again leave one set of transitions, not two.
            window.DataContext = new Features.States.LoadingViewModel(db.Services);
            Pump();
            Assert.Single(track.Transitions!);
            Assert.Single(knob.Transitions!);
            Assert.Equal(2, thumb.Transitions!.Count);

            // …and a re-resolve lets go of the settings it held. A second shell's MotionSettings takes over with
            // «Анимации» off there: everything withdraws, and the shine parks (a second sweep loop would go on
            // writing the transform, since Stop only cancels the source the control still holds).
            window.DataContext = new Features.States.LoadingViewModel(other.Services);
            Pump();
            Assert.False(moved.Enabled);
            Assert.Null(track.Transitions);
            Assert.Null(knob.Transitions);
            Assert.Null(thumb.Transitions);
            await SettleAsync(() => false, 200);
            Assert.Equal(-140, ShineAt(shine), 3);
            Assert.Null(shine.RenderTransform); // the sweep handed the transform back to the styles

            // The first settings no longer own these controls — a handler left there is what would keep a closed
            // window's controls alive — and the ones they moved to do.
            motion.Enabled = false;
            motion.Enabled = true;
            Pump();
            Assert.Null(track.Transitions);
            Assert.Null(knob.Transitions);
            Assert.Null(thumb.Transitions);

            moved.Enabled = true;
            Pump();
            Assert.NotNull(track.Transitions);
            Assert.NotNull(knob.Transitions);
            Assert.NotNull(thumb.Transitions);
        }
        finally
        {
            motion.Enabled = false;
            moved.Enabled = false;
            app.SetMotion(false);
        }
        Pump(); // drain the stop the finally asked for: the sweep's continuation belongs to this test, not the next
    }

    /// <summary>Pins T10 R9's invariant on the file itself: a transition or animation that a style sheet cannot
    /// withdraw has no business in Motion.axaml, so no selector there may reach into a template. Comments are
    /// stripped first — the rule is written down in that file, and saying it must not break the scan.</summary>
    [Fact]
    public void Motion_Styles_Never_Reach_Into_A_Template()
    {
        var path = System.IO.Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Theme", "Motion.axaml");
        var markup = Regex.Replace(File.ReadAllText(path), "<!--.*?-->", "", RegexOptions.Singleline);
        Assert.Contains("<Styles", markup); // the scan read the right file
        Assert.DoesNotContain("/template/", markup);
    }

    private static Border Part(Control control, string name) =>
        control.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);

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
        // Both switches go back, not just the styles: a control-owned loop (a skeleton's shine in this shell's
        // loading view) follows MotionSettings, and would go on sweeping in this window for the rest of the session.
        finally { db.Services.Motion.Enabled = false; app.SetMotion(false); }
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
        finally { db.Services.Motion.Enabled = false; app.SetMotion(false); }
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

    [AvaloniaFact]
    public async Task Dialog_Animates_Open_And_Close_And_Keeps_Content_While_Closing()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        db.Services.Motion.Enabled = true;
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        try
        {
            app.SetMotion(true);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            Pump();
            var host = window.GetVisualDescendants().OfType<Dialogs.DialogHostView>().Single();
            var root = host.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "Root");
            var card = host.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Card");

            var dialog = new Dialogs.ConfirmDialogViewModel("t", "m", "ok", false);
            var shown = shell.Dialogs.ShowAsync(dialog);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // animated values are sampled on a render tick
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(root.IsVisible);
            Assert.True(shell.Dialogs.HasDialog);
            Assert.True(card.Opacity < 1, "the card starts transparent and fades in");
            Settle(400);
            Assert.Equal(1.0, card.Opacity, 2);

            dialog.CancelCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(shell.Dialogs.HasDialog);          // Escape/hotkeys see «no dialog» at once
            Assert.Same(dialog, shell.Dialogs.Current);      // but the content stays for the 120 ms fade-out
            Assert.True(root.IsVisible);
            Assert.False(await shown);
            Settle(400);
            Assert.Null(shell.Dialogs.Current);
            Assert.False(root.IsVisible);
            AssertNoBindingErrors();
        }
        finally { db.Services.Motion.Enabled = false; app.SetMotion(false); }
    }

    [AvaloniaFact]
    public void Theme_Toggle_Crossfades_A_Snapshot_Of_The_Old_Theme()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        db.Services.Motion.Enabled = true;
        var theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.Theme = theme;
        var shell = new ShellViewModel(db.Services);
        try
        {
            app.SetMotion(true);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            Pump();
            var snapshot = window.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "ThemeSnapshot");
            Assert.False(snapshot.IsVisible);
            var before = Application.Current!.ActualThemeVariant;
            var fallbacks = ThemeCrossfade.FallbackCount;

            shell.ToggleThemeCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(before, Application.Current.ActualThemeVariant); // the switch itself is immediate
            Assert.Equal(fallbacks, ThemeCrossfade.FallbackCount);              // RenderTargetBitmap worked in headless Skia
            Assert.True(snapshot.IsVisible);
            Assert.NotNull(snapshot.Source);
            Settle(500);
            Assert.False(snapshot.IsVisible);
            Assert.Null(snapshot.Source);
        }
        finally { db.Services.Motion.Enabled = false; app.SetMotion(false); }
    }

    /// <summary>The landing is awaited rather than slept for: a zoom transition is driven by compositor frames, and
    /// the headless clock only produces those while something is dirty, so the number of frames a fixed sleep gets
    /// is not fixed. What is deterministic is that the value cannot overshoot its own first frame, and that it ends
    /// on the target — on all three targets: scale, offset X and offset Y each ride their own DoubleTransition, and
    /// the offsets cover a 200× larger range, so waiting on Scale alone once captured an OffsetX still in flight
    /// (one failure in seven full-suite runs).</summary>
    [AvaloniaFact]
    public async Task Zoom_Animates_Towards_The_Target_When_Asked()
    {
        var app = (App)Application.Current!;
        try
        {
            app.SetMotion(true);
            var content = new Border { Width = 400, Height = 300, Background = Brushes.Gray };
            var panel = new ZoomPanel { Child = content, AnimateZoom = true };
            var window = new Window { Width = 200, Height = 150, Content = panel, SizeToContent = SizeToContent.Manual };
            window.Show();
            Pump();
            Assert.Equal(0.5, panel.Scale, 6);

            // Where ZoomIn() is headed, from the same arithmetic the panel itself uses (it anchors on the middle
            // of the viewport), so the settle below waits for the whole view and not just the scale.
            var target = ZoomMath.ZoomAt(panel.Scale, panel.OffsetX, panel.OffsetY, 1.25, panel.Bounds.Width / 2, panel.Bounds.Height / 2);
            panel.ZoomIn();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the transition samples on render ticks
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotNull(panel.Transitions); // a zoom step glides…
            Assert.True(panel.Scale < 0.5 * 1.25, "the transition is still running");
            await SettleAsync(() => Math.Abs(panel.Scale - target.Scale) < 1e-6
                                    && Math.Abs(panel.OffsetX - target.OffsetX) < 1e-6
                                    && Math.Abs(panel.OffsetY - target.OffsetY) < 1e-6);
            Assert.Equal(0.5 * 1.25, panel.Scale, 3);
            Assert.Equal(target.OffsetX, panel.OffsetX, 6);
            Assert.Equal(target.OffsetY, panel.OffsetY, 6);
            Assert.Equal(0.5 * 1.25, ((MatrixTransform)content.RenderTransform!).Matrix.M11, 3);

            var (ox, oy) = (panel.OffsetX, panel.OffsetY);
            window.MouseDown(new Point(50, 50), Avalonia.Input.MouseButton.Left);
            window.MouseMove(new Point(70, 60));
            window.MouseUp(new Point(70, 60), Avalonia.Input.MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(panel.Transitions);          // …a drag never does: it must follow the pointer exactly
            Assert.Equal(ox + 20, panel.OffsetX, 6);
            Assert.Equal(oy + 10, panel.OffsetY, 6);
        }
        finally { app.SetMotion(false); }
    }

    /// <summary>A window that closes mid-crossfade produces no more compositor frames, so the fade's await never
    /// returns and its finally never runs: the ~8 MB snapshot would stay alive and the theme service would keep
    /// switching through a dead window. MainWindow tears both down when it closes (T10-R3 c).</summary>
    [AvaloniaFact]
    public void Closing_The_Window_Frees_The_Snapshot_And_Unhooks_The_Theme()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        db.Services.Motion.Enabled = true;
        var theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.Theme = theme;
        var shell = new ShellViewModel(db.Services);
        try
        {
            app.SetMotion(true);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            Pump();
            var snapshot = window.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "ThemeSnapshot");
            Assert.NotNull(theme.Transition);

            shell.ToggleThemeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(snapshot.Source); // the 220 ms fade is in flight

            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(snapshot.Source);    // freed without ever finishing the fade
            Assert.False(snapshot.IsVisible);
            Assert.Null(theme.Transition);
        }
        finally { db.Services.Motion.Enabled = false; app.SetMotion(false); }
    }

    [AvaloniaFact]
    public void Skeleton_Renders_Without_Binding_Errors()
    {
        using var db = TestDb.Create();
        var window = new Window { Width = 400, Height = 200, DataContext = new Features.States.LoadingViewModel(db.Services), Content = new Features.States.LoadingView() };
        window.Show();
        Pump();
        Assert.Equal(6, window.GetVisualDescendants().OfType<Skeleton>().Count());
        SetTheme(Avalonia.Styling.ThemeVariant.Dark);
        Frames.Capture(window, "loading-dark");
        AssertNoBindingErrors();
    }

    /// <summary>Where the skeleton's shine actually sits: the theme parks it at −140 (Margin, never animated) and
    /// Skeleton.cs sweeps it from there with TranslateTransform.X — so the park plus the transform, and a shine that
    /// has given the transform back reads as parked again.</summary>
    private static double ShineAt(Border shine) => shine.Margin.Left + (shine.RenderTransform?.Value.M31 ?? 0);

    /// <summary>Spec §7's three looping animations: the skeleton shine sweeps out of its −140 px park (owned by the
    /// control, T10 R9), a burning homework dot breathes below full opacity, the plan highlight's glow pulses (both
    /// from Theme/Motion.axaml). Motion is flipped the way production flips it — MotionSettings first, which the
    /// controls listen to, then the style sheet App keeps in step with it. Each animation is waited for rather than
    /// sampled at a fixed moment, and with motion off again every property is back where the theme puts it.</summary>
    [AvaloniaFact]
    public async Task Looping_Animations_Run_Only_While_Motion_Is_On()
    {
        var app = (App)Application.Current!;
        using var db = TestDb.Create();
        var motion = db.Services.Motion;
        var skeleton = new Skeleton { Width = 200 };
        var dot = new Ellipse { Classes = { "hwdot" } };
        var glow = new Border { Width = 40, Height = 20, Classes = { "mapglow" } };
        var window = new Window
        {
            Width = 300, Height = 200,
            DataContext = new Features.States.LoadingViewModel(db.Services), // MotionSettings.Resolve walks up to it
            Content = new StackPanel { Children = { skeleton, new Button { Classes = { "hw", "burning" }, Content = dot }, glow } }
        };
        window.Show();
        Pump();
        var shine = skeleton.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Shine");
        Assert.Equal(-140, ShineAt(shine), 3);
        Assert.Equal(1.0, dot.Opacity, 3);
        Assert.Equal(1.0, glow.Opacity, 3);
        try
        {
            motion.Enabled = true;
            app.SetMotion(true);
            Dispatcher.UIThread.RunJobs();
            await SettleAsync(() => ShineAt(shine) > -140 && dot.Opacity < 1 && glow.Opacity < 1);
            Assert.True(ShineAt(shine) > -140, $"the skeleton shine must sweep; it sits at {ShineAt(shine)}");
            Assert.True(dot.Opacity < 1, $"a burning homework dot must breathe; opacity = {dot.Opacity}");
            Assert.True(glow.Opacity < 1, $"the plan glow must pulse; opacity = {glow.Opacity}");
        }
        finally
        {
            motion.Enabled = false;
            app.SetMotion(false);
        }
        Pump();
        // Switching «Анимации» off must stop all three where the theme wants them, and keep them there: a loop that
        // goes on writing after its style is gone would leave the shine frozen across the bar — and never stop.
        Assert.Equal(-140, ShineAt(shine), 3);
        Assert.Equal(1.0, dot.Opacity, 3);
        Assert.Equal(1.0, glow.Opacity, 3);
        await SettleAsync(() => false, 200); // …and it stays parked while the clock keeps ticking
        Assert.Equal(-140, ShineAt(shine), 3);
        Assert.Null(shine.RenderTransform); // the sweep handed the transform back to the styles
    }
}
