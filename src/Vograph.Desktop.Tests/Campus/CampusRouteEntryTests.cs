using Vograph.Core.Campus;
using Vograph.Core.Models;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class CampusRouteEntryTests
{
    private static readonly DateTime Mon8 = new(2026, 9, 7, 8, 0, 0);

    private static (MapsViewModel Vm, FakeMapFiles Files) Make(TestDb db, CampusGraph? graph = null)
    {
        var files = new FakeMapFiles(Path.Combine(db.Dir, "maps"));
        files.EnsureFails = true;
        db.Services.MapFiles = files;
        var shell = new ShellViewModel(db.Services);
        var vm = new MapsViewModel(db.Services, shell, () => Mon8, graph);
        return (vm, files);
    }

    private static CampusGraph EntranceGraph() => CampusGraph.Load("""
        {"version":1,"buildings":["УЛК"],"nodes":[
          {"id":"lab.entrance","kind":"entrance","building":"УЛК","floor":1,"x":0.1,"y":0.9,"label":"Вход лабиринта"},
          {"id":"lab.room.a","kind":"room","building":"УЛК","floor":1,"x":0.2,"y":0.2,"room":"A1"},
          {"id":"lab.room.b","kind":"room","building":"УЛК","floor":1,"x":0.8,"y":0.2,"room":"B1"}
        ],"edges":[{"from":"lab.entrance","to":"lab.room.a","kind":"walk","seconds":10,"oneWay":false}]}
        """);

    private static CampusGraph Authored() =>
        CampusGraph.Load(File.ReadAllText(Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Assets", "maps", "campus-graph.json")));

    [Fact]
    public void Bundled_graph_lists_entrances_and_stays_unmarked_until_ends_resolve()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, Authored());
        Assert.NotEmpty(vm.Entrances);
        Assert.True(vm.HasEntrances);
        Assert.True(vm.IsRouteUnmarked);
        Assert.Equal("маршрут ещё не размечен", vm.RouteUnmarked);
        Assert.Contains(vm.Entrances, e => e.Label is "Вход ГК" or "Вход УЛК");
    }

    [Fact]
    public void Picker_lists_only_entrance_nodes_from_loaded_graph()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, EntranceGraph());
        var item = Assert.Single(vm.Entrances);
        Assert.Equal("lab.entrance", item.Id);
        Assert.Equal("Вход лабиринта", item.Label);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void Route_from_chosen_entrance_applies_steps_when_both_ends_resolve()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, EntranceGraph());
        vm.SelectEntranceCommand.Execute(Assert.Single(vm.Entrances));
        vm.SetRouteEnds("A1*", previousRoomKey: null);
        Assert.False(vm.IsRouteUnmarked);
        Assert.True(vm.HasRouteSteps);
        Assert.Contains(vm.RouteSteps, s => s.Text.Contains("коридор", StringComparison.OrdinalIgnoreCase) || s.Text.Contains("Пройдите"));
        Assert.True(Assert.Single(vm.Entrances).IsSelected);
    }

    [Fact]
    public void Grouped_route_instructions_keep_their_own_floor_and_building_targets()
    {
        var graph = CampusGraph.Load("""
            {"version":1,"buildings":["УЛК","ГК"],"nodes":[
              {"id":"entry","kind":"entrance","building":"УЛК","floor":1,"x":0.1,"y":0.1},
              {"id":"j1","kind":"junction","building":"УЛК","floor":1,"x":0.2,"y":0.1},
              {"id":"s1","kind":"stair","building":"УЛК","floor":1,"x":0.3,"y":0.1,"group":"s"},
              {"id":"s3","kind":"stair","building":"УЛК","floor":2,"x":0.3,"y":0.1,"group":"s"},
              {"id":"ulk-link","kind":"building_link","building":"УЛК","floor":2,"x":0.4,"y":0.1},
              {"id":"gk-link","kind":"building_link","building":"ГК","floor":4,"x":0.4,"y":0.1},
              {"id":"j4","kind":"junction","building":"ГК","floor":4,"x":0.5,"y":0.1},
              {"id":"room","kind":"room","building":"ГК","floor":4,"x":0.6,"y":0.1,"room":"493"}
            ],"edges":[
              {"from":"entry","to":"j1","kind":"walk","seconds":10,"oneWay":false},
              {"from":"j1","to":"s1","kind":"walk","seconds":10,"oneWay":false},
              {"from":"s1","to":"s3","kind":"stair_up","seconds":20,"oneWay":false},
              {"from":"s3","to":"ulk-link","kind":"walk","seconds":10,"oneWay":false},
              {"from":"ulk-link","to":"gk-link","kind":"building_link","seconds":30,"oneWay":false},
              {"from":"gk-link","to":"j4","kind":"walk","seconds":10,"oneWay":false},
              {"from":"j4","to":"room","kind":"walk","seconds":10,"oneWay":false}
            ]}
            """);
        using var db = TestDb.Create();
        var (vm, _) = Make(db, graph);
        vm.SelectEntranceCommand.Execute(Assert.Single(vm.Entrances));
        vm.SetRouteEnds("493", previousRoomKey: null);

        Assert.Equal(new[] { ("УЛК", 1), ("УЛК", 2), ("УЛК", 2), ("ГК", 4), ("ГК", 4) },
            vm.RouteSteps.Select(step => (step.Building, step.Floor)));
        Assert.Contains("2", vm.RouteSteps[1].Text);
        Assert.Contains("ГК", vm.RouteSteps[3].Text);
        Assert.Equal(7, vm.Route!.Legs.Count);
    }

    [Fact]
    public void Cabinet_without_room_node_stays_unmarked()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, EntranceGraph());
        vm.SelectEntranceCommand.Execute(Assert.Single(vm.Entrances));
        vm.SetRouteEnds("9999", previousRoomKey: null);
        Assert.True(vm.IsRouteUnmarked);
        Assert.False(vm.HasRouteSteps);
        Assert.False(vm.HasPath);
    }

    [Fact]
    public void Last_entrance_persists_under_maps_cache()
    {
        using var db = TestDb.Create();
        var (vm, files) = Make(db, EntranceGraph());
        vm.SelectEntranceCommand.Execute(Assert.Single(vm.Entrances));
        var path = Path.Combine(files.CacheDir, "last-entrance.txt");
        Assert.True(File.Exists(path));
        Assert.Equal("lab.entrance", File.ReadAllText(path).Trim());

        var (vm2, _) = Make(db, EntranceGraph());
        Assert.True(Assert.Single(vm2.Entrances).IsSelected);
    }

    [Fact]
    public void StartFor_rewrites_unreachable_hostel_to_ulk_main()
    {
        Assert.Equal("ulk.entrance.main",
            MapsComposer.StartFor(Authored(), "ulk.entrance.hostel", "ulk.room.564")?.Id);
    }

    [Fact]
    public void StartFor_without_a_requested_start_does_not_invent_an_entrance()
    {
        Assert.Null(MapsComposer.StartFor(Authored(), null, "ulk.room.564"));
    }

    [Fact]
    public void Unreachable_ulk_hostel_falls_back_to_main_and_explains_it()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, Authored());
        var hostel = Assert.Single(vm.Entrances, e => e.Id == "ulk.entrance.hostel");
        vm.SelectEntranceCommand.Execute(hostel);
        vm.SetRouteEnds("ulk.room.564", previousRoomKey: null);

        Assert.False(vm.IsRouteUnmarked);
        Assert.NotEmpty(vm.Route!.Legs);
        Assert.Contains(vm.Route.Legs, l => l.Kind == "stair_up");
        var toast = Assert.Single(db.Services.Toasts.Items);
        Assert.Contains("Вход УЛК", toast.Text, StringComparison.Ordinal);
        Assert.Contains("пройти нельзя", toast.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Previous_room_node_is_from_instead_of_entrance()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, EntranceGraph());
        vm.SetRouteEnds("A1*", previousRoomKey: "A1*");
        Assert.False(vm.IsRouteUnmarked); // same node: empty success, not unmarked
        Assert.False(vm.HasRouteSteps);
    }

    [Theory]
    [InlineData("401", "401*", "УЛК", "ГК")]
    [InlineData("401*;", "401;", "ГК", "УЛК")]
    public void Lesson_room_markers_route_between_the_correct_buildings(
        string destination, string previous, string fromBuilding, string toBuilding)
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, Authored());
        vm.SetRouteEnds(destination, previous);

        Assert.False(vm.IsRouteUnmarked);
        Assert.NotEmpty(vm.Route!.Legs);
        Assert.Equal(fromBuilding, vm.Route.Legs[0].Building);
        Assert.Equal(toBuilding, vm.Route.Legs[^1].Building);
        Assert.Contains(vm.Route.Legs, leg => leg.Kind == "building_link");
    }

    [Fact]
    public void Ambiguous_classroom_stays_unmarked_instead_of_using_first_match()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, Authored());
        vm.SelectEntranceCommand.Execute(vm.Entrances.Single(e => e.Id == "ulk.entrance.main"));
        vm.SetRouteEnds("319*", null);

        Assert.True(vm.IsRouteUnmarked);
    }

    [Fact]
    public void Previous_lesson_today_is_the_started_one_before_target()
    {
        var first = new Lesson { TimeStart = "09:00", TimeEnd = "10:35", ClassroomRaw = "493;" };
        var second = new Lesson { TimeStart = "12:40", TimeEnd = "14:15", ClassroomRaw = "563*;" };
        var mondayNoon = new DateTime(2026, 9, 7, 12, 0, 0);
        Assert.Same(first, MapsComposer.PreviousLessonToday([first, second], second, mondayNoon, mondayNoon));
        Assert.Null(MapsComposer.PreviousLessonToday([first, second], first, Mon8, Mon8));
        Assert.Null(MapsComposer.PreviousLessonToday([first, second], second, Mon8, Mon8));
    }

    [Fact]
    public void Next_tomorrow_does_not_use_todays_earlier_room_as_from()
    {
        var todayRoom = new Lesson { TimeStart = "09:00", TimeEnd = "10:35", ClassroomRaw = "A1*" };
        var tomorrow = new Lesson { TimeStart = "10:45", TimeEnd = "12:20", ClassroomRaw = "A1*" };
        var mondayEve = new DateTime(2026, 9, 7, 18, 0, 0);
        var tuesday = new DateTime(2026, 9, 8, 10, 45, 0);
        Assert.Null(MapsComposer.PreviousLessonToday([todayRoom], tomorrow, mondayEve, tuesday));

        using var db = TestDb.Create();
        var (vm, _) = Make(db, EntranceGraph());
        vm.SelectEntranceCommand.Execute(Assert.Single(vm.Entrances));
        var prev = MapsComposer.PreviousLessonToday([todayRoom], tomorrow, mondayEve, tuesday);
        vm.SetRouteEnds("A1*", previousRoomKey: prev?.ClassroomRaw);
        Assert.True(vm.HasRouteSteps); // last entrance → A1, not same-room leftover from today's 09:00
        Assert.False(vm.IsRouteUnmarked);
    }

    [Fact]
    public async Task TrackNext_without_chosen_entrance_opens_the_plan()
    {
        using var db = TestDb.Create();
        var (vm, _) = Make(db, Authored());
        await vm.TrackNextAsync();
        Assert.Equal(MapMode.NextLesson, vm.Mode);
        Assert.NotEmpty(vm.Entrances);
        Assert.StartsWith("Следующая пара · 493", vm.ContextLine);
        Assert.Equal(("ГК", 4), (vm.Current!.Building, vm.Current.Floor));
    }
}
