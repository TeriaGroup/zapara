using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Maps;

public sealed partial class MapsViewModel
{
    [ObservableProperty] private int currentRouteStepIndex;
    public string RouteStepHeader => RouteSteps.Count == 0 ? "Шаги маршрута" :
        $"Шаг {Math.Clamp(CurrentRouteStepIndex, 0, RouteSteps.Count - 1) + 1} из {RouteSteps.Count}: " +
        RouteSteps[Math.Clamp(CurrentRouteStepIndex, 0, RouteSteps.Count - 1)].Text;
    public bool CanAdvanceRouteStep => CurrentRouteStepIndex + 1 < RouteSteps.Count;
    public bool CanBackRouteStep => CurrentRouteStepIndex > 0 && RouteSteps.Count > 0;
    partial void OnCurrentRouteStepIndexChanged(int value) => NotifyRouteProgress();
    private void NotifyRouteProgress()
    {
        OnPropertyChanged(nameof(RouteStepHeader));
        OnPropertyChanged(nameof(CanAdvanceRouteStep));
        OnPropertyChanged(nameof(CanBackRouteStep));
    }
    [RelayCommand] private Task AdvanceRouteStep() => CanAdvanceRouteStep
        ? SelectRouteStep(RouteSteps[CurrentRouteStepIndex + 1]) : Task.CompletedTask;
    [RelayCommand] private Task BackRouteStep() => CanBackRouteStep
        ? SelectRouteStep(RouteSteps[CurrentRouteStepIndex - 1]) : Task.CompletedTask;
}
