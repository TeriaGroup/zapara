using Avalonia;
using Avalonia.Animation;
using Avalonia.Data;
using Avalonia.Styling;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>Spec §7 «Смена раздела»: the old page fades out, then a short gap, then the new one fades in.
/// Neither page translates — an 8 px slide re-rasters Inter every frame and reads as shaking text. The two
/// pages never share a frame. Duration zero = instant switch.</summary>
public sealed class FadeSlide : IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(180);
    public TimeSpan Gap { get; set; } = TimeSpan.FromMilliseconds(80);

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (Duration == TimeSpan.Zero)
        {
            if (to is not null) to.IsVisible = true;
            if (from is not null) from.IsVisible = false;
            // Navigating with animations on and then turning them off used to leave the last page pinned on
            // Opacity/RenderTransform; ClearValue hands both back so the next section draws at rest.
            Release(from);
            Release(to);
            return;
        }

        var pins = new List<IDisposable?>();
        // TransitioningContentControl has already shown `to`. Hide it until the outgoing page has left,
        // otherwise both sections paint on top of each other for the whole Duration.
        if (to is not null) to.IsVisible = false;
        try
        {
            if (from is not null)
            {
                pins.Add(Pin(from, opacity: 1));
                await Animate(fromOpacity: 1, toOpacity: 0).RunAsync(from, cancellationToken);
                foreach (var pin in pins) pin?.Dispose();
                pins.Clear();
                Release(from);
                from.IsVisible = false;
            }
            if (cancellationToken.IsCancellationRequested) return;
            if (Gap > TimeSpan.Zero)
            {
                try { await Task.Delay(Gap, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
            if (to is not null)
            {
                to.IsVisible = true;
                pins.Add(Pin(to, opacity: 0));
                await Animate(fromOpacity: 0, toOpacity: 1).RunAsync(to, cancellationToken);
            }
        }
        finally
        {
            foreach (var pin in pins) pin?.Dispose();
            Release(from);
            Release(to);
            if (from is not null && !cancellationToken.IsCancellationRequested) from.IsVisible = false;
        }
    }

    private static void Release(Visual? visual)
    {
        visual?.ClearValue(Visual.RenderTransformProperty);
        visual?.ClearValue(Visual.OpacityProperty);
    }

    /// <summary>Opacity is pinned at animation priority so the first painted frame is already faded. FillMode
    /// alone does not apply the first keyframe until a tick, which is the flash this class exists to stop.</summary>
    private static IDisposable? Pin(Visual visual, double opacity) =>
        visual.SetValue(Visual.OpacityProperty, opacity, BindingPriority.Animation);

    private Animation Animate(double fromOpacity, double toOpacity) => new()
    {
        Duration = Duration,
        Easing = MotionSettings.Ease,
        FillMode = FillMode.Both,
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, fromOpacity) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, toOpacity) } },
        }
    };
}
