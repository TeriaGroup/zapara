using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>Spec §7 «Смена раздела»: the old page fades out sliding 8px away, the new one fades in sliding 8px into
/// place; the same shape as Avalonia's PageSlide, with the design's curve. Duration zero = instant switch.</summary>
public sealed class FadeSlide : IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(180);
    public double Offset { get; set; } = 8;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (Duration == TimeSpan.Zero)
        {
            if (to is not null) to.IsVisible = true;
            if (from is not null) from.IsVisible = false;
            // The instant switch releases the transform too: navigating with animations on and then turning them
            // off used to leave the last animated page pinned at translateX(±8px), so the next section was drawn
            // 8 px off-centre for as long as the window lived.
            Release(from);
            Release(to);
            return;
        }
        var tasks = new List<Task>();
        if (from is not null)
            tasks.Add(Animate(from, fromOpacity: 1, toOpacity: 0, fromX: 0, toX: forward ? -Offset : Offset).RunAsync(from, cancellationToken));
        if (to is not null)
        {
            to.IsVisible = true;
            tasks.Add(Animate(to, fromOpacity: 0, toOpacity: 1, fromX: forward ? Offset : -Offset, toX: 0).RunAsync(to, cancellationToken));
        }
        try { await Task.WhenAll(tasks); }
        finally
        {
            // The animator sets RenderTransform itself, at local priority, and never gives it back: the leftover
            // translate would shadow every style-driven transform inside the page (a card's hover lift, a
            // button's :pressed scale) for good. Same release as Appear's.
            Release(from);
            Release(to);
        }
        if (from is not null && !cancellationToken.IsCancellationRequested) from.IsVisible = false;
    }

    private static void Release(Visual? visual) => visual?.ClearValue(Visual.RenderTransformProperty);

    private Animation Animate(Visual target, double fromOpacity, double toOpacity, double fromX, double toX) => new()
    {
        Duration = Duration,
        Easing = MotionSettings.Ease,
        Children =
        {
            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, fromOpacity), new Setter(TranslateTransform.XProperty, fromX) } },
            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, toOpacity), new Setter(TranslateTransform.XProperty, toX) } },
        }
    };
}
