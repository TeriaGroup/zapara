using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

public partial class ScheduleView : UserControl
{
    private ScheduleViewModel? _vm;
    private int _generation;

    public ScheduleView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook(DataContext as ScheduleViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Hook(null);
    }

    private void Hook(ScheduleViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null) _vm.DayShown -= OnDayShown;
        _vm = vm;
        if (_vm is not null) _vm.DayShown += OnDayShown;
    }

    /// <summary>Spec §7 «контент дня — кроссфейд + сдвиг 12px в сторону листания», 200 ms.</summary>
    private async void OnDayShown(int direction)
    {
        if (direction == 0 || _vm is not { } vm) return;
        var duration = vm.Motion.Duration(200);
        if (duration == TimeSpan.Zero) return;
        // Everything the continuation needs is read before the await: a detach runs Hook(null), and a catch block
        // that dereferenced the nulled _vm would throw an NRE out of an async void method — straight to the process.
        var (log, body) = (vm.App.Log, Body);
        var generation = ++_generation;
        try
        {
            await new Animation
            {
                Duration = duration, Easing = MotionSettings.Ease,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, 12d * direction) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d) } },
                }
            }.RunAsync(body);
        }
        catch (Exception ex)
        {
            log.Warn($"day transition: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // The transform animator writes RenderTransform at local priority and never releases it (T9-R4);
            // hand it back unless a newer day change already owns the panel.
            if (generation == _generation) body.ClearValue(Visual.RenderTransformProperty);
        }
    }
}
