using Vograph.Core.Campus;
using Vograph.Core.Models;
using Vograph.Desktop.Features.Schedule;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleTransitionUx300Tests
{
    [Fact]
    public void Authored_route_longer_than_actual_break_is_tight()
    {
        var graph = new CampusGraph(1, ["ГК"],
            [new Node("gk.room.101", "room", "ГК", 1, .1, .1, Room: "101"),
             new Node("gk.room.102", "room", "ГК", 1, .9, .9, Room: "102")],
            [new Edge("gk.room.101", "gk.room.102", "walk", 900, false)]);

        var assessment = ScheduleTransitionPlanner.Assess(graph, "10:30", "10:35", "101", "102");

        Assert.Equal("tight", assessment.Status);
        Assert.Equal(300, assessment.AvailableSeconds);
        Assert.Equal(900, assessment.RouteSeconds);
    }

    [Fact]
    public void Alternative_overlapping_lessons_do_not_invent_a_tight_transfer_to_the_next_slot()
    {
        var graph = new CampusGraph(1, ["ГК"],
            [new Node("a", "room", "ГК", 1, .1, .1, Room: "101"),
             new Node("b", "room", "ГК", 1, .2, .2, Room: "102"),
             new Node("c", "room", "ГК", 1, .3, .3, Room: "103")],
            [new Edge("a", "c", "walk", 30, false), new Edge("b", "c", "walk", 900, false)]);
        static LessonRow Row(string room, string start, string end) => new(
            new Lesson { ClassroomRaw = room }, start, end, null, "Пара", null, null, "", "Преподаватель", room,
            null, false, false, false, [], [], null);
        var a = Row("101", "09:00", "10:00"); var b = Row("102", "09:00", "10:00");
        var c = Row("103", "10:02", "11:30");

        foreach (var ordered in new[] { new[] { a, b, c }, new[] { b, a, c } })
        {
            var warnings = ScheduleTransitionPlanner.Warnings(graph, ordered);
            Assert.Contains(warnings, warning => warning.Contains("пересекаются"));
            Assert.DoesNotContain(warnings, warning => warning.Contains("времени мало"));
        }
    }
}
