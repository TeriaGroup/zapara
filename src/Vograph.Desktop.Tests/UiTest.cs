using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Base for headless UI tests: captures binding errors, switches themes, pumps the dispatcher.</summary>
public abstract class UiTest
{
    protected CapturingLogSink Sink { get; } = new();

    protected UiTest() => Logger.Sink = Sink;

    /// <summary>Longest transition in the theme (segmented thumb) plus a margin.</summary>
    private const int SettleMs = 260;

    protected static void SetTheme(ThemeVariant variant)
    {
        Application.Current!.RequestedThemeVariant = variant;
        Pump();
    }

    /// <summary>
    /// Runs queued dispatcher work and lets running transitions finish. The headless animation clock
    /// is wall-clock time and only samples on a render tick, so a frame captured right after a theme
    /// switch would otherwise show every brush mid-crossfade.
    /// </summary>
    protected static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        for (var elapsed = 0; elapsed <= SettleMs; elapsed += 20)
        {
            Thread.Sleep(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    protected void AssertNoBindingErrors() => Assert.Empty(Sink.Warnings);

    /// <summary>Clicks the center of a control through the window's input pipeline (not RaiseEvent).</summary>
    protected static void Click(Window window, Control target)
    {
        Dispatcher.UIThread.RunJobs();
        var p = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("target is not in the window's visual tree");
        window.MouseMove(p);
        window.MouseDown(p, Avalonia.Input.MouseButton.Left);
        window.MouseUp(p, Avalonia.Input.MouseButton.Left);
        Pump();
    }
}
