using Avalonia;
using Avalonia.Headless.XUnit;
using Vograph.Core.Campus;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusStackProjectorTests
{
    [Fact]
    public void Labyrinth_west3_to_east3_is_down_across_up_not_floor3_shortcut()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route!;
        var lines = CampusStackProjector.RoutePolylines(route, "УЛК");

        Assert.Contains(lines, IsDescending);
        Assert.Contains(lines, l => l.Kind == "walk" && l.Floor == 1 && IsHorizontal(l) && l.Points.Any(p => p is { X: 0.5, Y: 0.5 }));
        Assert.Contains(lines, IsAscending);

        var floor3 = lines.Where(l => l.Kind == "walk" && l.Floor == 3).ToList();
        Assert.True(floor3.Count is 1 or 2);
        Assert.Equal(2, floor3.Count);
        Assert.DoesNotContain(floor3, SpansWestToEastOnFloor3);
        Assert.DoesNotContain(lines, l => l.Kind == "walk" && l.Floor == 3 && SpansWestToEastOnFloor3(l));
    }

    [Fact]
    public void Lower_floor_walks_are_painted_before_higher_floor_rasters()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route!;
        var scene = CampusStackProjector.Project(route, "УЛК", 400, 400);
        var seq = CampusStackProjector.PaintSequence(scene).ToList();
        var walk1 = seq.FindIndex(s => s.Kind == "walk" && s.Floor == 1);
        var floor3 = seq.FindIndex(s => s.Kind == "floor" && s.Floor == 3);
        Assert.True(walk1 >= 0 && floor3 >= 0, $"walk1={walk1} floor3={floor3}");
        Assert.True(walk1 < floor3, "floor-1 corridor is drawn on top of floor 3");
    }

    [Fact]
    public void Empty_route_has_no_path_polylines()
    {
        Assert.Empty(CampusStackProjector.RoutePolylines(null, "УЛК"));
        var empty = CampusRouter.Find(
            CampusGraph.Load("""{"version":1,"buildings":["ГК","УЛК"],"nodes":[],"edges":[]}"""),
            "a", "b").Route;
        Assert.Null(empty);
        Assert.Empty(CampusStackProjector.RoutePolylines(empty, "ГК"));
    }

    [Fact]
    public void Stair_segments_use_shaft_xy_and_floor_as_z()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route!;
        var stairs = CampusStackProjector.RoutePolylines(route, "УЛК")
            .Where(l => l.Kind is "stair_up" or "stair_down")
            .ToList();
        Assert.NotEmpty(stairs);
        Assert.All(stairs, s =>
        {
            Assert.Equal(2, s.Points.Count);
            Assert.Equal(s.Floor, s.Points[0].Z, 6);
            Assert.Equal(s.ToFloor!.Value, s.Points[1].Z, 6);
            Assert.Equal(s.Points[0].X, s.Points[1].X, 6);
            Assert.Equal(s.Points[0].Y, s.Points[1].Y, 6);
        });
        Assert.Contains(stairs, s => s.Points[0] is { X: 0.2, Y: 0.5 } && s.Points[0].Z > s.Points[1].Z);
        Assert.Contains(stairs, s => s.Points[0] is { X: 0.8, Y: 0.5 } && s.Points[0].Z < s.Points[1].Z);
    }

    [Fact]
    public void Project_is_static_by_default_and_yaw_changes_2d_not_3d()
    {
        var route = CampusRouter.Find(SyntheticLabyrinth.Build(), "lab.room.west.3", "lab.room.east.3").Route!;
        var a = CampusStackProjector.Project(route, "УЛК", 400, 300);
        var b = CampusStackProjector.Project(route, "УЛК", 400, 300);
        Assert.Equal(a.Polylines.SelectMany(p => p.Points2), b.Polylines.SelectMany(p => p.Points2));
        Assert.Equal(a.Floors.Select(f => f.Floor), b.Floors.Select(f => f.Floor));

        var spun = CampusStackProjector.Project(route, "УЛК", 400, 300, yaw: CampusStackProjector.DefaultYaw + 0.4);
        Assert.NotEqual(a.Polylines.SelectMany(p => p.Points2), spun.Polylines.SelectMany(p => p.Points2));
        Assert.Equal(
            a.Polylines.SelectMany(p => p.Points),
            spun.Polylines.SelectMany(p => p.Points));
    }

    [Fact]
    public void Higher_floor_projects_above_lower_floor()
    {
        var scene = CampusStackProjector.Project(null, "УЛК", 400, 400);
        var y1 = AverageY(scene.Floors.Single(f => f.Floor == 1));
        var y3 = AverageY(scene.Floors.Single(f => f.Floor == 3));
        Assert.True(y3 < y1, $"floor 3 y={y3} should sit above floor 1 y={y1}");
        Assert.Empty(scene.Polylines);
    }

    [Fact]
    public void MapStack_copy_is_schema_etazhey()
    {
        Assert.Equal("Схема этажей", new I18nService().T("mapStack"));
    }

    [Fact]
    public void Stack_defaults_off_and_toggle_flips()
    {
        using var db = TestDb.Create();
        var files = new FakeMapFiles(Path.Combine(db.Dir, "maps"));
        files.EnsureFails = true;
        db.Services.MapFiles = files;
        var shell = new ShellViewModel(db.Services);
        var vm = new MapsViewModel(db.Services, shell, () => new DateTime(2026, 9, 7, 8, 0, 0));
        Assert.False(vm.ShowStack);
        vm.ToggleStackCommand.Execute(null);
        Assert.True(vm.ShowStack);
        vm.ToggleStackCommand.Execute(null);
        Assert.False(vm.ShowStack);
        Assert.True(vm.IsRouteUnmarked);
        Assert.Empty(CampusStackProjector.RoutePolylines(vm.Route, vm.ShownBuilding));
    }

    [AvaloniaFact]
    public void FloorRasterPaths_covers_each_cached_floor_of_the_building()
    {
        using var db = TestDb.Create();
        var files = new FakeMapFiles(Path.Combine(db.Dir, "maps"), ("УЛК", 1), ("УЛК", 3), ("УЛК", 5));
        db.Services.MapFiles = files;
        var paths = MapsComposer.FloorRasterPaths("УЛК", db.Services.Maps.GetAllMaps(), files.LocalPath);
        Assert.Equal(3, paths.Count);
        Assert.Equal(new[] { 1, 3, 5 }, paths.Keys.OrderBy(k => k));
        Assert.All(paths.Values, p => Assert.True(File.Exists(p)));
        Assert.Empty(MapsComposer.FloorRasterPaths("ГК", db.Services.Maps.GetAllMaps(), files.LocalPath));
    }

    [AvaloniaFact]
    public async Task Stack_receives_more_than_one_floor_image_when_several_plans_are_cached()
    {
        using var db = TestDb.Create();
        var files = new FakeMapFiles(Path.Combine(db.Dir, "maps"), ("УЛК", 1), ("УЛК", 2), ("УЛК", 3));
        db.Services.MapFiles = files;
        var shell = new ShellViewModel(db.Services);
        var vm = new MapsViewModel(db.Services, shell, () => new DateTime(2026, 9, 7, 8, 0, 0));
        await vm.ShowLessonMapAsync(db.Services.Maps.Resolve("320*;")!, "Физика");
        Assert.NotNull(vm.Image);
        Assert.Equal(new PixelSize(200, 100), vm.Image!.PixelSize);
        Assert.True(vm.StackFloorImages.Count >= 2, $"expected several stack rasters, got {vm.StackFloorImages.Count}");
        Assert.Contains(1, vm.StackFloorImages.Keys);
        Assert.Contains(2, vm.StackFloorImages.Keys);
        Assert.Contains(3, vm.StackFloorImages.Keys);
        Assert.All(vm.StackFloorImages.Values, b => Assert.True(b.PixelSize.Width > 0 && b.PixelSize.Height > 0));
    }

    [Fact]
    public void Stack_view_has_no_engine_or_auto_orbit()
    {
        var root = ResourceKeysTests.RepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "Vograph.Desktop", "Features", "Maps", "CampusStackView.cs"));
        Assert.DoesNotContain("UnityEngine", view, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenTK", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Silk.NET", view, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", view, StringComparison.Ordinal);
        Assert.DoesNotContain("unreal", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowOrbit", view, StringComparison.Ordinal);
        Assert.Contains("PathTrace.At", view, StringComparison.Ordinal);
        Assert.Contains("CampusStackProjector", view, StringComparison.Ordinal);
        Assert.Contains("if (!AllowOrbit)", view, StringComparison.Ordinal);
        Assert.Contains("FloorImages", view, StringComparison.Ordinal);
        Assert.Contains("PaintSequence", view, StringComparison.Ordinal);
        Assert.DoesNotContain("quad.Floor == ImageFloor", view, StringComparison.Ordinal);
    }

    private static bool IsDescending(StackPolyline line) =>
        line.Points.Zip(line.Points.Skip(1), (a, b) => a.Z > b.Z).Any(v => v);

    private static bool IsAscending(StackPolyline line) =>
        line.Points.Zip(line.Points.Skip(1), (a, b) => a.Z < b.Z).Any(v => v);

    private static bool IsHorizontal(StackPolyline line) =>
        line.Points.Count >= 2 && line.Points.All(p => Math.Abs(p.Z - line.Points[0].Z) < 1e-9);

    private static bool SpansWestToEastOnFloor3(StackPolyline line)
    {
        if (line.Points.Count == 0) return false;
        var minX = line.Points.Min(p => p.X);
        var maxX = line.Points.Max(p => p.X);
        return minX <= 0.25 && maxX >= 0.75;
    }

    private static double AverageY(StackQuad floor) => floor.Points2.Average(p => p.Y);
}
