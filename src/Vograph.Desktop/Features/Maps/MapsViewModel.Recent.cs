using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Maps;

public sealed record MapRouteChoice(string StartId, string DestinationId, string Label);

public sealed partial class MapsViewModel
{
    public ObservableCollection<MapRouteChoice> RecentRoutes { get; } = [];
    public ObservableCollection<MapPlaceChoice> PinnedPlaces { get; } = [];
    public bool HasRecentRoutes => RecentRoutes.Count > 0;
    public bool HasPinnedPlaces => PinnedPlaces.Count > 0;

    private void RememberRecentRoute()
    {
        if (manualStartId is not { } start || manualDestinationId is not { } destination) return;
        var old = RecentRoutes.FirstOrDefault(route => route.StartId == start && route.DestinationId == destination);
        if (old is not null) RecentRoutes.Remove(old);
        RecentRoutes.Insert(0, new(start, destination, PlaceLabel(start) + " → " + PlaceLabel(destination)));
        while (RecentRoutes.Count > 6) RecentRoutes.RemoveAt(RecentRoutes.Count - 1);
        OnPropertyChanged(nameof(HasRecentRoutes));
    }

    [RelayCommand] private void ClearRecentRoutes()
    { RecentRoutes.Clear(); OnPropertyChanged(nameof(HasRecentRoutes)); }

    [RelayCommand]
    private async Task RepeatRecentRoute(MapRouteChoice? route)
    {
        if (route is null || !RecentRoutes.Contains(route)) return;
        var start = _graph.Nodes.FirstOrDefault(node => node.Id == route.StartId && node.Kind == "room");
        var destination = _graph.Nodes.FirstOrDefault(node => node.Id == route.DestinationId && node.Kind == "room");
        if (start is null || destination is null)
        {
            RecentRoutes.Remove(route); OnPropertyChanged(nameof(HasRecentRoutes));
            App.Toasts.Info("Место из прежнего маршрута больше недоступно.");
            return;
        }
        manualStartId = start.Id; manualDestinationId = destination.Id;
        _prevRoomKey = start.Id; _destRoomKey = destination.Id;
        Mode = MapMode.Manual; NotifyManualRoute(); ComputeRoute();
        if (Route is not null) await ShowRoutePoint(destination.Id);
    }

    [RelayCommand]
    private void PinPlace(MapPlaceChoice? place)
    {
        if (place is null || _graph.Nodes.All(node => node.Id != place.Id || node.Kind != "room") ||
            PinnedPlaces.Any(item => item.Id == place.Id)) return;
        if (PinnedPlaces.Count >= 8) { App.Toasts.Info("Можно закрепить не больше восьми мест."); return; }
        PinnedPlaces.Add(place); OnPropertyChanged(nameof(HasPinnedPlaces));
    }
    [RelayCommand]
    private void UnpinPlace(MapPlaceChoice? place)
    {
        if (place is null) return;
        var found = PinnedPlaces.FirstOrDefault(item => item.Id == place.Id);
        if (found is null) return;
        PinnedPlaces.Remove(found); OnPropertyChanged(nameof(HasPinnedPlaces));
    }
    private void ClearPinnedPlaces()
    { PinnedPlaces.Clear(); OnPropertyChanged(nameof(HasPinnedPlaces)); }
}
