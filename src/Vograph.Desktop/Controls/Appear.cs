using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
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
/// (a past lesson card stays at 0.6) and releases the animated values.
/// </summary>
public static class Appear
{
    public static readonly AttachedProperty<AppearKind> KindProperty = AvaloniaProperty.RegisterAttached<Control, AppearKind>("Kind", typeof(Appear));
    public static readonly AttachedProperty<int> IndexProperty = AvaloniaProperty.RegisterAttached<Control, int>("Index", typeof(Appear));
    public static readonly AttachedProperty<bool> CascadeHostProperty = AvaloniaProperty.RegisterAttached<Control, bool>("CascadeHost", typeof(Appear));
    private static readonly AttachedProperty<DateTime> HostSinceProperty = AvaloniaProperty.RegisterAttached<Control, DateTime>("HostSince", typeof(Appear));

    private const int MaxCascadeItems = 8;
    private static readonly TimeSpan CascadeWindow = TimeSpan.FromMilliseconds(400);

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

    private static void OnHostAttached(object? sender, VisualTreeAttachmentEventArgs e) => ((Control)sender!).SetValue(HostSinceProperty, DateTime.UtcNow);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var control = (Control)sender!;
        var motion = MotionSettings.Resolve(control);
        if (!motion.Enabled) return;
        switch (GetKind(control))
        {
            case AppearKind.Fade:
                Run(control, delayMs: 0, offsetY: 0, ms: 180);
                break;
            case AppearKind.SlideUp:
                Run(control, delayMs: 0, offsetY: 12, ms: 200);
                break;
            case AppearKind.Cascade:
                var index = GetIndex(control);
                if (index >= MaxCascadeItems) return;
                var host = control.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetCascadeHost);
                if (host is null || DateTime.UtcNow - host.GetValue(HostSinceProperty) > CascadeWindow) return;
                Run(control, delayMs: 40 * index, offsetY: 8, ms: 240);
                break;
        }
    }

    private static async void Run(Control control, int delayMs, double offsetY, int ms)
    {
        var target = control.Opacity; // the styled value (0.6 for a past lesson card): the run ends there, not at 1
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            Easing = MotionSettings.Ease,
            FillMode = FillMode.Backward, // hold the first frame through the delay, release everything when done
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, offsetY) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, target), new Setter(TranslateTransform.YProperty, 0d) } },
            }
        };
        try { await animation.RunAsync(control); }
        catch (Exception)
        {
            // A control torn down mid-animation (navigation away) is not an error; nothing to log per card.
        }
    }
}
