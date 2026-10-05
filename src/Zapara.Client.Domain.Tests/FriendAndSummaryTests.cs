using Vograph.Core.Models;
using Zapara.Contracts.Sync;
using Xunit;
using static Zapara.Client.Domain.Tests.ScheduleRulesTests;

namespace Zapara.Client.Domain.Tests;

public class FriendAndSummaryTests
{
    [Theory]
    [InlineData("493", "ГК", 100)]
    [InlineData("401", "ГК", 75)]
    [InlineData("280", "ГК", 50)]
    [InlineData("320", "УЛК", 25)]
    public void Overlapping_lessons_have_existing_room_floor_building_scores(string room, string building, int expected)
    {
        var mine = Lesson(); mine.RoomRaw = "493"; mine.BuildingRaw = "ГК";
        var other = Lesson(); other.RoomRaw = room; other.BuildingRaw = building; other.TimeEnd = "";
        Assert.Equal(expected, FriendRules.Score(mine, other));
        other.TimeStart = "10:35";
        Assert.Equal(0, FriendRules.Score(mine, other));
        other.TimeStart = "09:00";
        other.BuildingRaw = "ГК";
        mine.RoomRaw = null!;
        other.RoomRaw = null!;
        Assert.Equal(50, FriendRules.Score(mine, other));
        mine.RoomRaw = "493"; mine.BuildingRaw = "ГК";
        other.RoomRaw = "493"; other.BuildingRaw = "УЛК";
        Assert.Equal(25, FriendRules.Score(mine, other));
        other.BuildingRaw = "ВЦ";
        Assert.Equal(100, FriendRules.Score(mine, other));
    }

    [Fact]
    public void Blank_friend_group_id_falls_back_to_the_group_name()
    {
        var lesson = Lesson(); lesson.RoomRaw = "493"; lesson.BuildingRaw = "ГК";
        var theirs = Lesson(group: "friend"); theirs.RoomRaw = "493"; theirs.BuildingRaw = "ГК";
        var s = new ScheduleSnapshot(Start, 2, [new Group { Id = "friend", Name = "А864С" }], [lesson, theirs], "v1");
        var visible = FriendRules.ForLesson(s, lesson, new(2026, 9, 14),
            [new FriendSchedule(new FriendValue("  ", "А864С", "Петя", 2, true), s)], strictness: 100);
        var hit = Assert.Single(visible);
        Assert.True(hit.Present);
        Assert.Equal(100, hit.Score);
    }

    [Fact]
    public void Friends_resolve_group_names_apply_threshold_and_limit_to_five_enabled()
    {
        var lesson = Lesson(); lesson.RoomRaw = "493"; lesson.BuildingRaw = "ГК";
        var theirs = Lesson(group: "friend"); theirs.RoomRaw = "401"; theirs.BuildingRaw = "ГК";
        var s = new ScheduleSnapshot(Start, 2, [new Group { Id = "friend", Name = "А864С" }], [lesson, theirs], "v1");
        var f = new FriendValue(null, "А864С", "Петя", 2, true);
        var friends = Enumerable.Range(0, 7).Select(_ => new FriendSchedule(f, s));
        Assert.Empty(FriendRules.ForLesson(s, lesson, new(2026, 9, 14), friends, strictness: 100));
        var visible = FriendRules.ForLesson(s, lesson, new(2026, 9, 14), friends, strictness: 75);
        Assert.Equal(5, visible.Count);
        Assert.All(visible, p => { Assert.True(p.Present); Assert.Equal(75, p.Score); });
        Assert.All(FriendRules.ForLesson(s, lesson, new(2026, 9, 14), friends, strictness: 100, alwaysShow: true), p => Assert.False(p.Present));
    }

    [Fact]
    public void Different_snapshot_and_missing_cache_are_not_claimed_as_intersections()
    {
        var mine = Snapshot(Lesson()) with { SnapshotId = "v1" };
        var f = new FriendValue("g", "А863С", "", 1, true);
        var results = FriendRules.ForLesson(mine, mine.Lessons[0], new(2026, 9, 14),
            [new(f, mine with { SnapshotId = "v2" }), new(f, null)], alwaysShow: true);
        Assert.Equal(2, results.Count);
        Assert.All(results, p => { Assert.False(p.Present); Assert.False(p.HasCompatibleSchedule); });
    }

    [Fact]
    public void Summary_groups_splits_teachers_and_honors_display_override_and_parity()
    {
        var a = Lesson(parity: 1); a.TypeRaw = "лек"; a.TeacherRaw = "Барт Е.Л.; Иванов С.П."; a.ClassroomRaw = "493; ";
        var b = Lesson(day: 3, parity: 0); b.TypeRaw = "пр"; b.TeacherRaw = "Барт Е.Л."; b.ClassroomRaw = "493";
        var c = Lesson(parity: 2);
        var s = Snapshot(a, b, c, Lesson(group: "other"));
        var summary = SummaryRules.Compose(s, "g", 1, displayName: _ => "Математика");
        Assert.Equal(2, summary.Total);
        Assert.Equal(new CountItem("Барт Е.Л.", 2), summary.Teachers[0]);
        Assert.Equal(new CountItem("493", 2), Assert.Single(summary.Rooms));
        Assert.Equal(new CountItem("Математика", 2), Assert.Single(summary.Subjects));
        Assert.Equal(6, summary.ByDay.Count);
        Assert.Equal(1, summary.ByDay[0].Count);
        Assert.Equal(1, summary.ByDay[2].Count);
        Assert.Contains(summary.ByType, x => x.Name == "лекция");
        Assert.Equal(3, SummaryRules.Compose(s, "g").Total);
        Assert.Equal(2, SummaryRules.Compose(s, "g", 1, invert: true).Total);
        var bare = Lesson(); bare.TeacherRaw = null!; bare.ClassroomRaw = null!;
        var missing = SummaryRules.Compose(Snapshot(bare), "g");
        Assert.Equal(1, missing.Total);
        Assert.Empty(missing.Teachers);
        Assert.Empty(missing.Rooms);
    }
}
