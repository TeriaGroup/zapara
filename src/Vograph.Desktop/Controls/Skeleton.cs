using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
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
        Listen(MotionSettings.Resolve(this));
        ApplyMotion();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Listen(null); // no settings, no sweep: a placeholder off screen animates nothing
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
        Sweep(_shine, duration, cts); // cannot throw: the whole body is guarded
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
    /// do it silently. Nothing after the await can throw, so a pass that ends on a torn-down dispatcher is inert.</summary>
    private async void Sweep(Border shine, TimeSpan duration, CancellationTokenSource cts)
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
            Warn("skeleton: the shine stopped sweeping", ex);
        }
        finally
        {
            cts.Dispose();
            // Stop() has already parked the shine and dropped this source; only a loop that ended on its own — an
            // error — still owns the transform the animator assigned at local priority.
            if (ReferenceEquals(_sweep, cts))
            {
                _sweep = null;
                shine.ClearValue(Visual.RenderTransformProperty);
            }
        }
    }

    /// <summary>The app log in production, the trace listeners anywhere else (tests, design time) — the same sink
    /// Appear uses, and for the same reason: a placeholder must not carry a dependency to say what went wrong.</summary>
    private static void Warn(string context, Exception ex)
    {
        var message = $"{context}: {ex.GetType().Name}: {ex.Message}";
        if (Application.Current is App { Services.Log: { } log }) log.Warn(message);
        else Trace.TraceWarning(message);
    }
}
