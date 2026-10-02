using Vograph.Core.Campus;
using Vograph.Core.Models;
using Vograph.Desktop.Features.Maps;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ClassroomActivityUx300Tests : UiTest
{
    private static CampusGraph Graph() => new(1, ["ГК", "УЛК"],
        [new Node("gk.320", "room", "ГК", 3, .2, .2, Room: "320"),
         new Node("ulk.320", "room", "УЛК", 3, .3, .3, Room: "320")], []);

    [Fact]
    public void Canonical_graph_node_separates_same_number_in_two_buildings_and_reports_coverage()
    {
        var date = new DateTime(2026, 9, 14);
        Group[] groups = [new() { Id = "a", Name = "Группа А" }, new() { Id = "b", Name = "Группа Б" },
            new() { Id = "unknown", Name = "Без копии" }];
        IReadOnlyList<Lesson> Schedule(string id) => id switch
        {
            "a" => [new Lesson { GroupId = id, TimeStart = "09:00", TimeEnd = "10:30", SubjectRaw = "Математика", ClassroomRaw = "320;" }],
            "b" => [new Lesson { GroupId = id, TimeStart = "09:00", TimeEnd = "10:30", SubjectRaw = "Физика", ClassroomRaw = "320*;" },
                    new Lesson { GroupId = id, TimeStart = "11:00", TimeEnd = "12:30", SubjectRaw = "Онлайн", ClassroomRaw = "дистанционно" }],
            _ => []
        };
        var result = ClassroomActivityPlanner.Create(Graph(), "gk.320", date, groups,
            group => group.Id != "unknown", Schedule);
        Assert.Equal(2, result.KnownGroups);
        Assert.Equal(3, result.TotalGroups);
        Assert.Equal(1, result.UnmatchedLessons);
        Assert.Equal("a", Assert.Single(result.Items).GroupId);
        Assert.Equal("Математика", result.Items[0].SubjectRaw);
    }

    [Fact]
    public async Task Guest_can_inspect_saved_groups_without_changing_own_group()
    {
        using var db = TestDb.Create();
        db.Services.Db.UpsertGroup(new Group { Id = "3314", Name = "Соседняя группа" });
        db.Services.Db.UpsertGroup(new Group { Id = "3315", Name = "Нет копии" });
        db.Services.Db.InsertLesson(new Lesson { GroupId = "3314", DayOfWeek = 1, Parity = 0,
            Index = 97, TimeStart = "09:00", TimeEnd = "10:30", SubjectRaw = "Физика",
            SubjectNormalized = "физика", ClassroomRaw = "320;" });
        var vm = new MapsViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14), Graph());
        vm.ChooseRoomActivityCommand.Execute(MapPlaceBrowse.Filter(Graph().Nodes, "320").Single(row => row.Building == "ГК"));
        await vm.LoadRoomActivityCommand.ExecuteAsync(null);
        Assert.Contains(vm.ActivityRows, row => row.Entry.GroupId == "3314");
        Assert.Contains("Проверено групп", vm.ActivityStatus);
        Assert.Equal(TestDb.MyGroupId, db.Services.Settings.MyGroupId);
    }
}
