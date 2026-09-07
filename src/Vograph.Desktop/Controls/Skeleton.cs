using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>
/// A placeholder bar (spec §3.3): a Chip-coloured block with a shine that sweeps across while «Анимации» is on.
/// The theme parks the shine 140 px off the clipped left edge and this control sweeps it by animating
/// TranslateTransform.X of PART_Shine. The sweep belongs to the control (T10 R9) for two reasons: a style animation
/// on a template part is never cancelled when Theme/Motion.axaml is removed — the shine swept on with «Анимации»
/// off, for the life of the process — and a transform costs no layout pass, unlike the Margin sweep it replaces,
/// which re-measured the placeholder on every frame.
/// </summary>
public class Skeleton : TemplatedControl
{
    /// <summary>How far the 120 px shine travels from its park (Margin −140): past the right edge of the widest
    /// placeholder in the app. Purely visual — the sweep is a transform, so it never touches layout.</summary>
    private const double SweepDistance = 500;

    private const int SweepMs = 1400;

    private Border? _shine;
    private MotionSettings? _motion;
    private CancellationTokenSource? _sweep;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Stop(); // a re-templated control leaves the shine it was sweeping parked
        _shine = e.NameScope.Find<Border>("PART_Shine");
        ApplyMotion();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Resolve();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Listen(null); // no settings, no sweep: a placeholder off screen animates nothing
        ApplyMotion();
    }

    /// <summary>Attach is not the only moment the answer changes: MotionSettings.Resolve reads the nearest view
    /// model up the tree, and three views here hand a DataContext to a view that is already attached
    /// (Dialogs/DialogHostView, Features/Maps/MapsView, MapFullscreenView) — a loading placeholder is exactly the
    /// thing on screen while a view model is still on its way. Resolving only on attach left such a skeleton on
    /// MotionSettings.Off — a shine parked for good — where the style animation this replaced needed no data
    /// context at all (T10 R10a).</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Resolve();
    }

    /// <summary>Safe to call as often as anything asks: Listen keeps at most one subscription, and Start refuses to
    /// spin up a second loop while one is running, so re-resolving neither doubles the sweep nor leaves a handler
    /// behind. A detached control resolves to nothing — it must not subscribe to the app-wide settings from outside
    /// the tree, and a placeholder off screen has nothing to animate.</summary>
    private void Resolve()
    {
        Listen(this.IsAttachedToVisualTree() ? MotionSettings.Resolve(this) : null);
        ApplyMotion();
    }

    /// <summary>Follows one MotionSettings while attached and none once detached: a handler left on the app-wide
    /// instance would keep this control — and the window it came with — alive. MotionSettings.Off never flips, so
    /// there is nothing to subscribe to there; the shine simply stays parked.</summary>
    private void Listen(MotionSettings? motion)
    {
        if (ReferenceEquals(motion, MotionSettings.Off)) motion = null;
        if (ReferenceEquals(_motion, motion)) return;
        if (_motion is not null) _motion.PropertyChanged -= OnMotionChanged;
        _motion = motion;
        if (_motion is not null) _motion.PropertyChanged += OnMotionChanged;
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(MotionSettings.Enabled)) ApplyMotion();
    }

    private void ApplyMotion()
    {
        if (_shine is null || _motion is not { Enabled: true }) Stop();
        else Start();
    }

    private void Start()
    {
        if (_sweep is not null || _shine is null) return; // already sweeping
        var duration = _motion?.Duration(SweepMs) ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero) return; // no duration is no animation, and no loop to spin in either
        var cts = new CancellationTokenSource();
        _sweep = cts;
        // The sink is read here, on the UI thread, and carried into the loop: the guards down there have to hold
        // wherever a continuation resumes, and a fresh Application.Current lookup from there is the one thing they
        // must not depend on.
        Sweep(_shine, duration, cts, Sink()); // cannot throw: the whole body is guarded
    }

    /// <summary>Ends the sweep and parks the shine on this frame, without waiting for the loop's own continuation:
    /// «Анимации» off has to be visible at once, not one dispatcher turn later. Cancelling releases the animated
    /// TranslateTransform.X, and ClearValue hands RenderTransform — which the transform animator assigns itself, at
    /// local priority — back to the styles; without that the shine would stay frozen halfway across the bar.</summary>
    private void Stop()
    {
        var cts = _sweep;
        _sweep = null;
        cts?.Cancel(); // the registration runs synchronously, which drops the animation's subscription
        _shine?.ClearValue(Visual.RenderTransformProperty);
    }

    /// <summary>One pass at a time, repeated until cancelled: Animation.RunAsync refuses an infinite IterationCount
    /// and IAnimation.Apply — what a style animation uses to loop — is internal to Avalonia. async void with a
    /// catch-all: nobody can await this, and a placeholder whose shine gives up must neither take the app down nor
    /// do it silently. Both the loop and its teardown are guarded, so no path out of this method can throw —
    /// which is what an async void continuation needs, whatever thread it resumes on (T10 R10b).</summary>
    private async void Sweep(Border shine, TimeSpan duration, CancellationTokenSource cts, Action<string> log)
    {
        var animation = new Animation
        {
            Duration = duration, // linear, on repeat: the same sweep the style used to run
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.XProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.XProperty, SweepDistance) } },
            }
        };
        try
        {
            while (!cts.IsCancellationRequested) await animation.RunAsync(shine, cts.Token);
        }
        catch (Exception ex)
        {
            Warn(log, "skeleton: the shine stopped sweeping", ex);
        }
        finally
        {
            // The finally needs a guard of its own: it runs from a continuation, where an escaping exception is an
            // unhandled one that takes the process down — and ClearValue below is exactly the call that would raise
            // one (VerifyAccess) if this continuation ever resumed off the UI thread.
            try
            {
                cts.Dispose();
                // Stop() has already parked the shine and dropped this source; only a loop that ended on its own —
                // an error — still owns the transform the animator assigned at local priority.
                if (ReferenceEquals(_sweep, cts))
                {
                    _sweep = null;
                    shine.ClearValue(Visual.RenderTransformProperty);
                }
            }
            catch (Exception ex)
            {
                Warn(log, "skeleton: the shine could not be parked", ex);
            }
        }
    }

    /// <summary>The app log in production, the trace listeners anywhere else (tests, design time) — the same sink
    /// Appear uses, and for the same reason: a placeholder must not carry a dependency to say what went wrong. Read
    /// once, in Start, on the UI thread, so that a sweep can say so from wherever it ends up.</summary>
    private static Action<string> Sink()
    {
        if (Application.Current is App { Services.Log: { } log }) return log.Warn;
        return static message => Trace.TraceWarning(message);
    }

    /// <summary>Writes through the sink Start captured, and never throws on the way out: both callers are guards of
    /// an async void continuation, so a sink that fails falls back to the trace listeners rather than escaping.</summary>
    private static void Warn(Action<string> log, string context, Exception ex)
    {
        var message = $"{context}: {ex.GetType().Name}: {ex.Message}";
        try { log(message); }
        catch (Exception failed) { Trace.TraceWarning($"{message} (and the log itself failed: {failed.GetType().Name}: {failed.Message})"); }
    }
}
