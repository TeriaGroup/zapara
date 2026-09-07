using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>
/// Toggle drawn as a pill switch. A ToggleButton subclass: no required template parts, no drag logic to fight.
/// Spec §7: the track cross-fades to the accent and the knob slides across, both in 150 ms. Those two transitions
/// are applied here, in code, from the nearest MotionSettings — they used to be «/template/» styles in
/// Theme/Motion.axaml, and Avalonia does not withdraw a style that reaches inside a template when the sheet is
/// removed (its detach never reaches the template children): with «Анимации» off the knob went on gliding and the
/// track on fading for the life of the control. A control owns its parts' motion (T10 R9).
/// </summary>
public class Switch : ToggleButton
{
    private Border? _track;
    private Border? _knob;
    private MotionSettings? _motion;

    protected override Type StyleKeyOverride => typeof(Switch);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // Find, not Get: the parts are what this control animates, not what it needs to work.
        _track = e.NameScope.Find<Border>("PART_Track");
        _knob = e.NameScope.Find<Border>("PART_Knob");
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
        Listen(null);
        ApplyMotion();
    }

    /// <summary>Follows one MotionSettings while attached and none once detached: a handler left on the app-wide
    /// instance would keep this control — and the window it came with — alive. MotionSettings.Off never flips, so
    /// there is nothing to subscribe to there; its parts simply never get transitions.</summary>
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

    /// <summary>Applies the two transitions while «Анимации» is on and withdraws them the moment it goes off.</summary>
    private void ApplyMotion()
    {
        if (_motion is not { Enabled: true } motion)
        {
            Withdraw(_track);
            Withdraw(_knob);
            return;
        }
        var duration = motion.Duration(150);
        if (_track is not null)
            _track.Transitions = new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty, Duration = duration, Easing = MotionSettings.Ease },
            };
        if (_knob is not null)
            _knob.Transitions = new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = MotionSettings.Ease },
            };
    }

    private static void Withdraw(Border? part) => part?.ClearValue(Animatable.TransitionsProperty);
}
