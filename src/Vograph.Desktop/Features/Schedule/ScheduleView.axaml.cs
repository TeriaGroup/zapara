using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Styling;
using Vograph.Desktop.Services;
using Vograph.Desktop.Controls;

namespace Vograph.Desktop.Features.Schedule;

public partial class ScheduleView : UserControl
{
    private ScheduleViewModel? _vm;
    private int _generation;
    private readonly PlannerTouchNavigation _swipe;

    public ScheduleView()
    {
        InitializeComponent();
        _swipe = new PlannerTouchNavigation(Body, () => _vm is { IsBusy: false },
            () => (_vm, _vm?.CalendarDate), direction =>
            {
                if (_vm is not { } vm) return;
                var command = direction > 0 ? vm.NextDayCommand : vm.PrevDayCommand;
                if (command.CanExecute(null)) command.Execute(null);
            });
        DataContextChanged += (_, _) => {Hook(DataContext as ScheduleViewModel);ApplyPlanningLayout();};
        SizeChanged+=(_,_)=>ApplyPlanningLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _swipe.Reset();
        Hook(null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hook(DataContext as ScheduleViewModel);
    }

    private void Hook(ScheduleViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        _swipe?.Reset();
        if (_vm is not null) _vm.DayShown -= OnDayShown;
        if (_vm is not null) _vm.LessonFocusRequested -= OnLessonFocusRequested;
        _vm = vm;
        if (_vm is not null) _vm.DayShown += OnDayShown;
        if (_vm is not null) _vm.LessonFocusRequested += OnLessonFocusRequested;
    }

    private void ApplyPlanningLayout()
    {
        var wide=Bounds.Width>=1150;
        PlannerColumns.ColumnDefinitions=new ColumnDefinitions(wide?"150,*,300":"*");
        PlannerColumns.RowDefinitions=new RowDefinitions(wide?"Auto":"Auto,Auto,Auto");
        Grid.SetRow(PlannerMain,wide?0:1);Grid.SetColumn(PlannerMain,wide?1:0);
        Grid.SetRow(PlannerDeadlines,wide?0:2);Grid.SetColumn(PlannerDeadlines,wide?2:0);
        _vm?.SetDateStripCount(Bounds.Width>0 && Bounds.Width<720?5:7);
    }

    private void OnOverlapClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ScheduleOverlap overlap) return;
        overlap.First.ShowDetails = true;
        FocusLesson(overlap.First);
    }
    private void OnLessonFocusRequested(LessonRowViewModel row) => Dispatcher.UIThread.Post(() => FocusLesson(row));
    private void FocusLesson(LessonRowViewModel row)
    {
        var card = this.GetVisualDescendants().OfType<LessonCardView>()
            .FirstOrDefault(control => ReferenceEquals(control.DataContext, row));
        card?.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsVisible && button.IsEnabled)?.Focus();
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
                Duration = duration, Easing = MotionSettings.Ease, FillMode = FillMode.Both,
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
