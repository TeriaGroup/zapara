using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Maps;

public sealed record OfflinePlanRow(MapInfo Map, bool Available)
{
    public string Label => $"{Map.Building}, {Map.Floor} этаж · " + (Available ? "план доступен без сети" : "нет локального плана");
    public bool CanPrepare => !Available;
}

public sealed partial class MapsViewModel
{
    [ObservableProperty] private IReadOnlyList<OfflinePlanRow> offlinePlans = [];
    private async Task RefreshOfflinePlansAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var maps = await RunAsync(() => App.Maps.GetAllMaps().Where(map => map.HasMap).ToArray(), "offline map catalog");
        if (maps is null || !operation.IsCurrent) return;
        var rows = await Task.Run(() => maps.Select(map => new OfflinePlanRow(map, App.MapFiles.LocalPath(map) is not null)).ToArray());
        if (operation.IsCurrent) OfflinePlans = rows;
    }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PrepareOfflinePlan(OfflinePlanRow? row)
    {
        if (row is null || !OfflinePlans.Contains(row) || !row.CanPrepare) return;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        string? path;
        try { path = await Task.Run(() => App.MapFiles.EnsureAsync(row.Map, operation.Token), operation.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        { path = null; }
        if (!operation.IsCurrent) return;
        await RefreshOfflinePlansAsync();
        App.Toasts.Info(path is null ? "План пока не удалось подготовить без сети." : "План доступен на устройстве.");
    }
}
