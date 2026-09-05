using Avalonia;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WindowBoundsLogicTests
{
    private static readonly PixelRect[] OneScreen = { new(0, 0, 1920, 1080) };
    private static readonly PixelSize Min = new(960, 600);

    [Fact]
    public void Null_Saved_Keeps_Defaults() => Assert.Null(WindowBoundsLogic.Restore(null, OneScreen, Min));

    [Fact]
    public void Visible_Bounds_Are_Returned_As_Is()
    {
        var saved = new WindowBounds(100, 80, 1280, 800, false);
        Assert.Equal(saved, WindowBoundsLogic.Restore(saved, OneScreen, Min));
    }

    [Fact]
    public void Offscreen_Bounds_Are_Rejected()
    {
        var saved = new WindowBounds(5000, 80, 1280, 800, false); // disconnected second monitor
        Assert.Null(WindowBoundsLogic.Restore(saved, OneScreen, Min));
    }

    [Fact]
    public void Too_Small_Bounds_Are_Enlarged_To_Minimum()
    {
        var saved = new WindowBounds(10, 10, 300, 200, false);
        var restored = WindowBoundsLogic.Restore(saved, OneScreen, Min)!;
        Assert.Equal(960, restored.Width);
        Assert.Equal(600, restored.Height);
        Assert.Equal(10, restored.X);
    }

    [Fact]
    public void Mostly_Offscreen_Bounds_Are_Rejected()
    {
        var saved = new WindowBounds(1800, 1000, 1280, 800, false); // only a corner visible
        Assert.Null(WindowBoundsLogic.Restore(saved, OneScreen, Min));
    }
}
