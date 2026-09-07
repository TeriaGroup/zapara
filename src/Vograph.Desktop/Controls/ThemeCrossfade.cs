using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>
/// Spec §7 «Смена темы»: render the window root into a bitmap, lay it over everything, switch the variant underneath,
/// fade the snapshot out (220 ms), drop it. Spec §11 fallback: when the snapshot cannot be rendered the switch happens
/// plainly and FallbackCount grows (tests read it; the log gets a WARN).
/// </summary>
public static class ThemeCrossfade
{
    public static int FallbackCount { get; private set; }

    public static async Task RunAsync(TopLevel top, Visual root, Image snapshot, Action apply, MotionSettings motion, AppLog? log = null)
    {
        var duration = motion.Duration(220);
        if (duration == TimeSpan.Zero || root.Bounds.Width < 1 || root.Bounds.Height < 1)
        {
            apply();
            return;
        }
        RenderTargetBitmap? bitmap = null;
        try
        {
            var scale = top.RenderScaling;
            bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(root.Bounds.Width * scale), (int)Math.Ceiling(root.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(root);
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            FallbackCount++;
            log?.Warn($"theme crossfade: snapshot failed, switching plainly: {ex.GetType().Name}: {ex.Message}");
            apply();
            return;
        }
        snapshot.Source = bitmap;
        snapshot.Opacity = 1;
        snapshot.IsVisible = true;
        apply(); // the new theme renders underneath while the old one fades away on top
        try
        {
            await new Animation // awaited inside try/catch so the whole method really cannot throw: ThemeService fires it and forgets
            {
                Duration = duration, Easing = MotionSettings.Ease, FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                }
            }.RunAsync(snapshot);
        }
        catch (Exception ex)
        {
            log?.Warn($"theme crossfade: the fade did not finish: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            snapshot.IsVisible = false;
            snapshot.Source = null;
            bitmap.Dispose();
        }
    }
}
