using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Vograph.Desktop.Tests;

/// <summary>Saves rendered headless frames as PNG so a human can inspect them.</summary>
public static class Frames
{
    public static string Dir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("VOGRAPH_FRAMES_DIR");
            var dir = string.IsNullOrWhiteSpace(env) ? Path.Combine(AppContext.BaseDirectory, "frames") : env;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string Save(WriteableBitmap frame, string name)
    {
        var path = Path.Combine(Dir, name + ".png");
        frame.Save(path);
        return path;
    }

    /// <summary>Runs pending layout/render work, captures the window and saves it.</summary>
    public static string Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Headless window produced no frame");
        return Save(frame, name);
    }
}
