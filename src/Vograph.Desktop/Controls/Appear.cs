using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

public enum AppearKind { None, Fade, SlideUp, Cascade }

/// <summary>
/// Spec §7 «Появление списка» and «Тост»: an attached behaviour that fades a control in when it enters the visual tree.
/// Cascade: opacity 0→1 and 8px→0 with a 40 ms × index delay for the first eight items — but only while the nearest
/// CascadeHost attached less than 400 ms ago, so a reload of an already visible list (a day change, a homework
/// toggle) does not replay the entrance. SlideUp: 12px→0 + fade (toasts). Every run ends at the control's own opacity
/// (a past lesson card stays at 0.6) and hands RenderTransform back to the styles.
/// </summary>
public static class Appear
{
    public static readonly AttachedProperty<AppearKind> KindProperty = AvaloniaProperty.RegisterAttached<Control, AppearKind>("Kind", typeof(Appear));
    public static readonly AttachedProperty<int> IndexProperty = AvaloniaProperty.RegisterAttached<Control, int>("Index", typeof(Appear));
    public static readonly AttachedProperty<bool> CascadeHostProperty = AvaloniaProperty.RegisterAttached<Control, bool>("CascadeHost", typeof(Appear));

    /// <summary>Environment.TickCount64 of the moment the host entered the visual tree. Monotonic on purpose:
    /// DateTime.UtcNow moves with the system clock and with NTP, and a backwards jump would replay entrances.</summary>
    private static readonly AttachedProperty<long> HostSinceProperty = AvaloniaProperty.RegisterAttached<Control, long>("HostSince", typeof(Appear));

    private const int MaxCascadeItems = 8;
    private const long CascadeWindowMs = 400;

    /// <summary>How many cascade entrances have been launched (never: how many were skipped). Tests count runs
    /// instead of sampling a running animation — the headless render clock samples once and stops in some
    /// processes, so an opacity read mid-flight is a fact about the clock, not about this code. Written only from
    /// the UI thread, where every attachment handler runs.</summary>
    internal static int CascadeRuns;

    static Appear()
    {
        KindProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.AttachedToVisualTree -= OnAttached;
            if (e.NewValue is AppearKind k && k != AppearKind.None) c.AttachedToVisualTree += OnAttached;
        });
        CascadeHostProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.AttachedToVisualTree -= OnHostAttached;
            if (e.NewValue is true) c.AttachedToVisualTree += OnHostAttached;
        });
    }

    public static AppearKind GetKind(Control c) => c.GetValue(KindProperty);
    public static void SetKind(Control c, AppearKind value) => c.SetValue(KindProperty, value);
    public static int GetIndex(Control c) => c.GetValue(IndexProperty);
    public static void SetIndex(Control c, int value) => c.SetValue(IndexProperty, value);
    public static bool GetCascadeHost(Control c) => c.GetValue(CascadeHostProperty);
    public static void SetCascadeHost(Control c, bool value) => c.SetValue(CascadeHostProperty, value);

    private static void OnHostAttached(object? sender, VisualTreeAttachmentEventArgs e) => ((Control)sender!).SetValue(HostSinceProperty, Environment.TickCount64);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var control = (Control)sender!;
        var kind = GetKind(control);
        var index = GetIndex(control);
        // The 8-item cap is decided before MotionSettings.Resolve, which walks the visual tree up to the window:
        // a 60-row list would otherwise pay 60 walks to find out that 52 of the rows never animate.
        if (kind == AppearKind.Cascade && index >= MaxCascadeItems) return;
        var motion = MotionSettings.Resolve(control);
        if (!motion.Enabled) return;
        switch (kind)
        {
            case AppearKind.Fade:
                Run(control, delay: TimeSpan.Zero, offsetY: 0, duration: motion.Duration(180));
                break;
            case AppearKind.SlideUp:
                Run(control, delay: TimeSpan.Zero, offsetY: 12, duration: motion.Duration(200));
                break;
            case AppearKind.Cascade:
                var host = control.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetCascadeHost);
                if (host is null || Environment.TickCount64 - host.GetValue(HostSinceProperty) > CascadeWindowMs) return;
                CascadeRuns++;
                Run(control, delay: TimeSpan.FromMilliseconds(40 * index), offsetY: 8, duration: motion.Duration(240));
                break;
        }
    }

    private static async void Run(Control control, TimeSpan delay, double offsetY, TimeSpan duration)
    {
        var target = control.Opacity; // the styled value (0.6 for a past lesson card): the run ends there, not at 1
        var start = new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d) } };
        var end = new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, target) } };
        var slide = offsetY != 0 ? Pin(control, offsetY) : null;
        if (slide is not null)
        {
            start.Setters.Add(new Setter(TranslateTransform.YProperty, offsetY));
            end.Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
        }
        var animation = new Animation
        {
            Duration = duration,
            Delay = delay,
            Easing = MotionSettings.Ease,
            FillMode = FillMode.Backward, // hold the first frame through the delay, release Opacity when done
            Children = { start, end }
        };
        try { await animation.RunAsync(control); }
        catch (Exception ex)
        {
            // A control torn down mid-animation (navigation away) is not an error — but it is not silent either.
            Warn($"appear: {GetKind(control)} on {control.GetType().Name} did not finish", ex);
        }
        finally
        {
            // Both halves of the release, and both are needed: the pinned group is ours to withdraw, and the
            // ClearValue keeps Task 9's rule for the local-priority group the animator itself would assign if
            // Pin ever came back empty-handed. A transform left at either priority shadows every style-driven
            // one for good (Border.card.hoverable:pointerover's 1px lift, a button's :pressed scale), i.e.
            // turning animations ON would take the interaction feedback away (T9-R4).
            slide?.Dispose();
            control.ClearValue(Visual.RenderTransformProperty);
        }
    }

    /// <summary>
    /// Hands the transform animator a group it can find, at animation priority. Avalonia's animator answers a
    /// TranslateTransform.Y keyframe by assigning a TransformGroup of its own to RenderTransform — a *local*
    /// value — and then reading the property back to look the group up; on every control whose RenderTransform
    /// carries a TransformOperationsTransition (Theme/Motion.axaml: Button, Border.card — the Week day buttons,
    /// the Summary cards, the lesson cards) that local write starts the transition, whose interpolated
    /// TransformOperations then wins the read. The animator logged «Cannot find the appropriate transform» and
    /// returned without animating: the slide half of the cascade never played, only the fade did (T12-R1).
    /// Animation priority is below the transition's trigger threshold, so nothing intercepts this one, and the
    /// returned handle takes it away again. Y starts at the offset so the first painted frame is already
    /// displaced, whatever the render clock does with the animation's own first tick.
    /// </summary>
    private static IDisposable? Pin(Control control, double offsetY) =>
        control.SetValue(Visual.RenderTransformProperty,
            new TransformGroup { Children = { new TranslateTransform { Y = offsetY } } },
            BindingPriority.Animation);

    /// <summary>The app log in production, the trace listeners anywhere else (tests, design time). A failed
    /// entrance must never be silent, and must not cost this behaviour a dependency of its own either.</summary>
    private static void Warn(string context, Exception ex)
    {
        var message = $"{context}: {ex.GetType().Name}: {ex.Message}";
        if (Application.Current is App { Services.Log: { } log }) log.Warn(message);
        else Trace.TraceWarning(message);
    }
}
