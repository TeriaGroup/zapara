using Avalonia.Controls;
using Vograph.Desktop.Controls;

namespace Vograph.Desktop.Features.Week;

public partial class WeekView : UserControl
{
    private readonly PlannerTouchNavigation _swipe;

    public WeekView()
    {
        InitializeComponent();
        _swipe = new PlannerTouchNavigation(WeekContent,
            () => DataContext is WeekViewModel { IsLoaded: true, HasGroup: true, IsBusy: false },
            () => (DataContext, (DataContext as WeekViewModel)?.ParityIndex),
            direction =>
            {
                if (DataContext is WeekViewModel vm) vm.ParityIndex = direction > 0 ? 1 : 0;
            });
        DataContextChanged += (_, _) => _swipe.Reset();
    }
}
