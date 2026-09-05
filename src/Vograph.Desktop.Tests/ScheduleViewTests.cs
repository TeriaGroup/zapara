using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleViewTests : UiTest
{
    [AvaloniaFact]
    public async Task Schedule_Renders_Rows_Empty_State_And_Reacts_To_Segment_Click()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => new DateTime(2026, 9, 7, 8, 0, 0));
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();

        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();

        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "schedule-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "schedule-light");

        var cards = window.GetVisualDescendants().OfType<LessonCardView>().ToList();
        Assert.Equal(2, cards.Count);
        Assert.Single(window.GetVisualDescendants().OfType<FriendDot>(), d => d.Fill == DotFill.Full);

        // Click "Вчера" in the segmented control through the input pipeline.
        var seg = window.GetVisualDescendants().OfType<SegmentedControl>().Single();
        var yesterday = seg.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First();
        Click(window, yesterday);
        await vm.ReloadAsync();

        Assert.Equal(-1, vm.DayOffset);
        Assert.True(vm.IsEmpty);
        Pump();
        Assert.Single(window.GetVisualDescendants().OfType<EmptyState>(), e => e.IsEffectivelyVisible);
        Frames.Capture(window, "schedule-empty-light");

        AssertNoBindingErrors();
    }
}
