using System.Diagnostics;
using System.Runtime.CompilerServices;
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
///
/// The whole body is guarded, because ThemeService fires this and forgets it: whatever goes wrong — the render, the
/// switch itself, the fade — the variant still ends up applied, the overlay still comes down and the bitmap is still
/// freed, and every failure is logged. Overlapping toggles are ordered by a run generation, so only the newest run
/// owns the shared snapshot image and a run that has been taken over cannot cut the live fade off. Teardown() frees
/// both when the window carrying the image goes away with a fade still in flight.
/// </summary>
public static class ThemeCrossfade
{
    public static int FallbackCount { get; private set; }

    /// <summary>State per snapshot image rather than per process: the app has one window, but the tests build
    /// several, and a run over one window's image must not declare itself the owner of another's.</summary>
    private static readonly ConditionalWeakTable<Image, RunState> States = new();

    public static async Task RunAsync(TopLevel top, Visual root, Image snapshot, Action apply, MotionSettings motion, AppLog? log = null)
    {
        var state = States.GetOrCreateValue(snapshot);
        var generation = ++state.Generation;
        Held? held = null;
        var applied = false;
        try
        {
            var duration = motion.Duration(220);
            if (duration == TimeSpan.Zero)
            {
                apply();
                applied = true;
                return;
            }
            if (root.Bounds.Width < 1 || root.Bounds.Height < 1)
            {
                // Nothing laid out to photograph (a variant switched before the window's first layout pass): the
                // plain switch, counted and logged like every other fallback rather than skipped in silence.
                FallbackCount++;
                Warn(log, $"nothing to snapshot ({root.Bounds.Width:F0}×{root.Bounds.Height:F0}), switching plainly");
                apply();
                applied = true;
                return;
            }
            RenderTargetBitmap bitmap;
            try
            {
                var scale = top.RenderScaling;
                bitmap = new RenderTargetBitmap(
                    new PixelSize((int)Math.Ceiling(root.Bounds.Width * scale), (int)Math.Ceiling(root.Bounds.Height * scale)),
                    new Vector(96 * scale, 96 * scale));
                bitmap.Render(root);
            }
            catch (Exception ex)
            {
                FallbackCount++;
                Warn(log, $"snapshot failed, switching plainly: {ex.GetType().Name}: {ex.Message}");
                apply();
                applied = true;
                return;
            }
            held = new Held(bitmap);
            var superseded = state.Live; // a previous run's bitmap leaves the screen on the next line: free it there
            state.Live = held;
            snapshot.Source = bitmap;
            snapshot.Opacity = 1;
            snapshot.IsVisible = true;
            superseded?.Free();
            apply(); // the new theme renders underneath while the old one fades away on top
            applied = true;
            await new Animation
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
            Warn(log, $"the crossfade did not finish: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // A throw between the snapshot and the switch would otherwise leave the window showing a photograph of
            // the old theme for good, with the variant never switched at all.
            if (!applied)
            {
                try { apply(); }
                catch (Exception ex) { Warn(log, $"the switch itself failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            if (generation == state.Generation) // else a newer toggle owns the image: its fade must not be cut off
            {
                snapshot.IsVisible = false;
                snapshot.Source = null;
                snapshot.ClearValue(Visual.OpacityProperty); // the fade's own value goes back to the styles (T9)
                if (ReferenceEquals(state.Live, held)) state.Live = null;
            }
            held?.Free();
        }
    }

    /// <summary>The window that owns the snapshot image is gone. An unfinished fade never comes back (a closed window
    /// produces no compositor frames), so nothing else would clear the image or free its ~8 MB bitmap.</summary>
    public static void Teardown(Image snapshot)
    {
        var state = States.GetOrCreateValue(snapshot);
        state.Generation++; // no run in flight owns the image any more
        snapshot.IsVisible = false;
        snapshot.Source = null;
        snapshot.ClearValue(Visual.OpacityProperty);
        var live = state.Live;
        state.Live = null;
        live?.Free();
    }

    /// <summary>A window without a shell has no AppLog, and a silent catch is not an option: fall back to the trace
    /// listeners, the same sink MotionSettings uses before the log exists.</summary>
    private static void Warn(AppLog? log, string message)
    {
        if (log is not null) log.Warn($"theme crossfade: {message}");
        else Trace.TraceWarning($"theme crossfade: {message}");
    }

    /// <summary>Which run owns one snapshot image, and the bitmap currently under it.</summary>
    private sealed class RunState
    {
        public int Generation;
        public Held? Live;
    }

    /// <summary>One bitmap, freed exactly once, by whichever of the paths above reaches it first.</summary>
    private sealed class Held(RenderTargetBitmap bitmap)
    {
        private RenderTargetBitmap? _bitmap = bitmap;

        public void Free()
        {
            var bitmap = _bitmap;
            _bitmap = null;
            bitmap?.Dispose();
        }
    }
}
