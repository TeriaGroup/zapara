using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SmokeTests
{
    [AvaloniaFact]
    public void Window_Renders_And_Frame_Is_Saved()
    {
        var window = new Window { Width = 400, Height = 300, Content = new TextBlock { Text = "Военмех" } };
        window.Show();

        var path = Frames.Capture(window, "smoke");

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 1000, "PNG is suspiciously small — did nothing render?");
    }
}
