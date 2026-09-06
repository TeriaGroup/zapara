using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Vograph.Desktop.Controls;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ZoomPanelTests : UiTest
{
    [Fact]
    public void ZoomAt_Keeps_The_Content_Point_Under_The_Anchor()
    {
        // content point under anchor: (anchor - offset) / scale
        var (s, ox, oy) = ZoomMath.ZoomAt(scale: 0.5, ox: 10, oy: 20, factor: 2, anchorX: 100, anchorY: 60);
        Assert.Equal(1.0, s);
        Assert.Equal((100 - 10) / 0.5, (100 - ox) / s, 6);
        Assert.Equal((60 - 20) / 0.5, (60 - oy) / s, 6);
    }

    [Fact]
    public void Zoom_Is_Clamped_And_Fit_Centers()
    {
        Assert.Equal(ZoomMath.Max, ZoomMath.ZoomAt(5, 0, 0, 10, 0, 0).Scale);
        Assert.Equal(ZoomMath.Min, ZoomMath.ZoomAt(0.3, 0, 0, 0.1, 0, 0).Scale);
        var fit = ZoomMath.Fit(200, 150, 400, 300);
        Assert.Equal((0.5, 0.0, 0.0), fit);
        var wide = ZoomMath.Fit(400, 150, 400, 300); // height limits: scale .5, content 200 wide → centered at x=100
        Assert.Equal((0.5, 100.0, 0.0), wide);
        Assert.Equal((1.0, 0.0, 0.0), ZoomMath.Fit(0, 0, 400, 300)); // no viewport yet
        Assert.Equal((-100.0, -75.0), ZoomMath.Centered(200, 150, 400, 300, 1));
    }

    [AvaloniaFact]
    public void Panel_Fits_On_First_Layout_Then_Wheel_Zooms_Around_Cursor_And_Drag_Pans()
    {
        var content = new Border { Width = 400, Height = 300, Background = Brushes.Gray };
        var panel = new ZoomPanel { Child = content };
        var window = new Window { Width = 200, Height = 150, Content = panel, SizeToContent = SizeToContent.Manual };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.5, panel.Scale, 6);
        Assert.Equal(0, panel.OffsetX, 6);
        Assert.Equal(0, panel.OffsetY, 6);

        window.MouseWheel(new Point(100, 75), new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.5 * 1.15, panel.Scale, 6);
        // the content point under (100,75) — (200,150) — is still under the cursor
        Assert.Equal(200, (100 - panel.OffsetX) / panel.Scale, 4);
        Assert.Equal(150, (75 - panel.OffsetY) / panel.Scale, 4);

        var (ox, oy) = (panel.OffsetX, panel.OffsetY);
        window.MouseDown(new Point(50, 50), MouseButton.Left);
        window.MouseMove(new Point(70, 60));
        window.MouseUp(new Point(70, 60), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ox + 20, panel.OffsetX, 6);
        Assert.Equal(oy + 10, panel.OffsetY, 6);

        panel.ResetScale();
        Assert.Equal(1, panel.Scale, 6);
        Assert.Equal(-100, panel.OffsetX, 6);
        Assert.Equal(-75, panel.OffsetY, 6);

        for (var i = 0; i < 20; i++) panel.ZoomIn();
        Assert.Equal(ZoomMath.Max, panel.Scale, 6);
        for (var i = 0; i < 40; i++) panel.ZoomOut();
        Assert.Equal(ZoomMath.Min, panel.Scale, 6);

        panel.Fit();
        Assert.Equal(0.5, panel.Scale, 6);
        var matrix = Assert.IsType<MatrixTransform>(content.RenderTransform).Matrix;
        Assert.Equal(0.5, matrix.M11, 6);
        Assert.Equal(panel.OffsetX, matrix.M31, 6);
        AssertNoBindingErrors();
    }
}
