using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.Summary;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Section host: chrome stays put while pages swap, and at the 960×600 minimum nothing runs off the content column.</summary>
public class ShellLayoutTests : UiTest
{
    private static readonly DateTime Mon7 = new(2026, 9, 7, 8, 0, 0);

    [AvaloniaFact]
    public async Task Sections_Do_Not_Overflow_The_Host_At_Min_Window()
    {
        using var db = Open();
        var shell = Shell(db);
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 960, Height = 600 };
        window.Show();
        Pump();

        var host = window.GetVisualDescendants().OfType<TransitioningContentControl>().Single(c => c.Name == "Host");
        Assert.True(host.ClipToBounds, "the 8px section slide must clip to the content column");
        var loaded = Loaded();
        var overflows = new List<string>();
        foreach (var key in Enum.GetValues<SectionKey>())
        {
            shell.NavigateTo(key);
            await Waits.Until(() => loaded[key](shell.Current!), $"section {key} loaded");
            Pump();
            overflows.AddRange(HorizontalOverflow(host, key));
        }

        Assert.True(overflows.Count == 0, "at 960×600 these controls run past the content column:\n" + string.Join("\n", overflows));
    }

    [AvaloniaFact]
    public async Task Section_Switch_Does_Not_Move_Chrome()
    {
        using var db = Open();
        db.Services.Motion.Enabled = true;
        ((App)Application.Current!).SetMotion(true);
        var shell = Shell(db);
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
        window.Show();
        Pump();

        var sidebar = window.GetVisualDescendants().OfType<SidebarView>().Single();
        var host = window.GetVisualDescendants().OfType<TransitioningContentControl>().Single(c => c.Name == "Host");
        var title = window.GetVisualDescendants().OfType<Grid>().First().Children.OfType<Border>().First();
        var origin = Snapshot(title, sidebar, host);

        foreach (var key in Enum.GetValues<SectionKey>())
        {
            if (key == shell.CurrentKey) continue;
            shell.NavigateTo(key);
            for (var i = 0; i < 12; i++)
            {
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var now = Snapshot(title, sidebar, host);
                if (now != origin)
                {
                    ((App)Application.Current!).SetMotion(false);
                    db.Services.Motion.Enabled = false;
                    Assert.Fail($"chrome moved while opening {key}: {origin} → {now}");
                }
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        ((App)Application.Current!).SetMotion(false);
        db.Services.Motion.Enabled = false;
    }

    private static TestDb Open()
    {
        var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.MapFiles = new FakeMapFiles(Path.Combine(db.Dir, "maps"), ("ГК", 4));
        db.Services.Launcher = new FakeLauncher();
        db.Services.FileDialogs = new FakeFileDialogs();
        db.Services.UpdateSource = new FakeUpdateSource();
        db.Services.Lecturers.LoadXmlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-lecturers.xml"))).GetAwaiter().GetResult();
        return db;
    }

    private static ShellViewModel Shell(TestDb db)
    {
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon7 };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Week, () => new WeekViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Summary, () => new SummaryViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Teachers, () => new TeachersViewModel(db.Services, shell, () => Mon7, allowNetwork: false));
        shell.Register(SectionKey.Maps, () => new MapsViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Friends, () => new FriendsViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Homework, () => new HomeworkViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Mon7));
        return shell;
    }

    private static Dictionary<SectionKey, Func<ViewModelBase, bool>> Loaded() => new()
    {
        [SectionKey.Schedule] = vm => ((ScheduleViewModel)vm).Lessons.Count == 2,
        [SectionKey.Week] = vm => ((WeekViewModel)vm).Days.Count == 6,
        [SectionKey.Summary] = vm => ((SummaryViewModel)vm).TotalText != "—",
        [SectionKey.Teachers] = vm => ((TeachersViewModel)vm).Items.Count > 0,
        [SectionKey.Maps] = vm => ((MapsViewModel)vm).Image is not null,
        [SectionKey.Friends] = vm => ((FriendsViewModel)vm).Friends.Count == 1,
        [SectionKey.Homework] = vm => ((HomeworkViewModel)vm).Groups.Count > 0,
        [SectionKey.Settings] = vm => ((SettingsViewModel)vm).GroupName == "А863С",
    };

    private static string Snapshot(Control title, Control sidebar, Control host) =>
        $"title={title.Bounds} sidebar={sidebar.Bounds} host={host.Bounds}";

    private static IEnumerable<string> HorizontalOverflow(Control host, SectionKey key)
    {
        var width = host.Bounds.Width;
        if (width <= 0) yield break;
        foreach (var scroller in host.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (!scroller.IsEffectivelyVisible) continue;
            if (scroller.Extent.Width > scroller.Viewport.Width + 1)
                yield return $"{key}: {scroller.GetType().Name} extent {scroller.Extent.Width:0.#} > viewport {scroller.Viewport.Width:0.#}";
        }
        foreach (var child in host.GetVisualDescendants().OfType<Control>())
        {
            if (!child.IsEffectivelyVisible || child.Bounds.Width <= 0 || child.Bounds.Height <= 0) continue;
            if (child.GetVisualAncestors().Any(a => a is Avalonia.Controls.Primitives.ScrollBar or Avalonia.Controls.Primitives.Thumb)) continue;
            if (child is Avalonia.Controls.Primitives.ScrollBar or Avalonia.Controls.Primitives.Thumb) continue;
            if (child is not (TextBox or Button or TextBlock or SegmentedControl or UserControl)) continue;
            if (child.TranslatePoint(new Point(0, 0), host) is not { } topLeft) continue;
            var right = topLeft.X + child.Bounds.Width;
            if (topLeft.X >= -2 && right <= width + 2) continue;
            var id = string.IsNullOrEmpty(child.Name) ? child.GetType().Name : child.Name;
            yield return $"{key}: {id} x={topLeft.X:0.#} w={child.Bounds.Width:0.#} host={width:0.#}";
        }
    }
}
