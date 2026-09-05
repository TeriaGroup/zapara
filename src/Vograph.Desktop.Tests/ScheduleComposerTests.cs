using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Schedule;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleComposerTests
{
    // Fixture calendar: semester starts Tue 2026-09-01; Mon 07.09 is odd (code 1), Tue 08.09 / Wed 09.09 / Mon 14.09 are even (code 2).
    private static readonly DateTime MonMorning = new(2026, 9, 7, 8, 0, 0);

    [Fact]
    public void Monday_Morning_Has_Two_Rows_With_Override_Homework_And_Friends()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(0, MonMorning);

        Assert.Equal("Сегодня", day.Title);
        Assert.Equal("Понедельник, 7 сентября · нечетная неделя · неделя 1 · 2 пары", day.Subtitle);
        Assert.Equal(2, day.Rows.Count);

        var math = day.Rows[0];
        Assert.Equal("09:00", math.TimeStart);
        Assert.Equal("10:35", math.TimeEnd);
        Assert.Equal("Матан", math.DisplayName);
        Assert.Equal("ВЫСШ. МАТЕМАТ", math.OriginalName);
        Assert.Equal("лекции — в 493", math.Note);
        Assert.Equal("лекция", math.TypeLabel);
        Assert.Equal("Барт Е.Л.", math.Teacher);
        Assert.Equal("493", math.RoomText);
        Assert.Equal("ГК", math.BuildingTag);
        Assert.Equal("след. 14.09", math.NextDateText); // next «лек ВЫСШ. МАТЕМАТ» is Mon 14.09 (even); Wednesday's «пр ВЫСШ. МАТЕМАТ» is a different Core key
        Assert.True(math.IsNext);
        Assert.False(math.IsPast);
        var hw = Assert.Single(math.Homework);
        Assert.Equal("§5, задачи 1–12", hw.Text);
        var friend = Assert.Single(math.Friends);
        Assert.Equal("09С31", friend.GroupName);
        Assert.Equal(DotFill.Full, friend.Fill);           // 09С31 also sits in 493 at 9:00
        Assert.Equal(0, friend.ColorIndex);
        Assert.Contains("в той же аудитории", friend.Tooltip);
        Assert.Contains("Иван", friend.Tooltip);

        var law = day.Rows[1];
        Assert.Equal("ОСН РОС ГОС", law.DisplayName);
        Assert.Null(law.OriginalName);
        Assert.Equal("563", law.RoomText);
        Assert.Equal("УЛК", law.BuildingTag);
        Assert.False(law.IsNext);
        Assert.Equal(DotFill.Half, Assert.Single(law.Friends).Fill); // same building УЛК (563* vs 227*), different floor
    }

    [Fact]
    public void Past_And_Next_Flags_Follow_The_Clock()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(0, new DateTime(2026, 9, 7, 11, 0, 0));
        Assert.True(day.Rows[0].IsPast);
        Assert.False(day.Rows[0].IsNext);
        Assert.True(day.Rows[1].IsNext);
    }

    [Fact]
    public void Sunday_Is_Empty_Without_Hint()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(-1, MonMorning);
        Assert.Empty(day.Rows);
        Assert.Equal("Вчера", day.Title);
        Assert.Equal("Воскресенье — пар нет", day.EmptyTitle);
        Assert.Null(day.EmptyHint);
    }

    [Fact]
    public void Empty_Weekday_Hints_At_Next_Lesson()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(1, MonMorning); // Tue 08.09, even: no lessons
        Assert.Empty(day.Rows);
        Assert.Equal("Пар нет", day.EmptyTitle);
        Assert.Equal("следующая пара — среда, 14:55", day.EmptyHint);
    }

    [Fact]
    public void Remote_Lesson_Has_No_Building_And_No_Map()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(-2, MonMorning); // Sat 05.09 odd
        var row = Assert.Single(day.Rows);
        Assert.True(row.IsRemote);
        Assert.Equal("дистанционно", row.RoomText);
        Assert.Null(row.BuildingTag);
    }

    [Fact]
    public void Friends_Hidden_Below_Strictness_Unless_Always_Show()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.IntersectionStrictness = 100; // only same room counts
        db.Services.Db.SaveSettings(s);
        var day = new ScheduleComposer(db.Services).Compose(0, MonMorning);
        Assert.Single(day.Rows[0].Friends);   // same room → shown
        Assert.Empty(day.Rows[1].Friends);    // same building only → hidden

        s.AlwaysShowAllTrafficLights = true;
        db.Services.Db.SaveSettings(s);
        day = new ScheduleComposer(db.Services).Compose(0, MonMorning);
        Assert.Equal(DotFill.Off, Assert.Single(day.Rows[1].Friends).Fill);
    }

    [Fact]
    public void InitialOffset_Uses_Smart_Start()
    {
        using var db = TestDb.Create();
        var composer = new ScheduleComposer(db.Services);
        Assert.Equal(0, composer.InitialOffset(MonMorning));
        Assert.Equal(1, composer.InitialOffset(new DateTime(2026, 9, 7, 15, 0, 0)));
    }

    [Fact]
    public void NextOccurrence_And_LessonsUntil()
    {
        using var db = TestDb.Create();
        var settings = db.Services.Db.GetSettings();
        Assert.Equal(new DateTime(2026, 9, 14), NextOccurrence.Find(db.Services.Db, settings, TestDb.MathSubject, new DateTime(2026, 9, 7))); // lecture → next lecture (Wed practice is another key)
        Assert.Equal(new DateTime(2026, 9, 14), NextOccurrence.Find(db.Services.Db, settings, TestDb.MathSubject, new DateTime(2026, 9, 9)));
        Assert.Null(NextOccurrence.Find(db.Services.Db, settings, "НЕТ ТАКОГО", new DateTime(2026, 9, 7)));

        var norm = Vograph.Core.Services.ParityService.NormalizeSubject(TestDb.MathSubject);
        Assert.Equal(0, HomeworkLabels.LessonsUntil(db.Services.Db, settings, norm, new DateTime(2026, 9, 5), new DateTime(2026, 9, 7)));
        Assert.Equal(1, HomeworkLabels.LessonsUntil(db.Services.Db, settings, norm, new DateTime(2026, 9, 5), new DateTime(2026, 9, 9)));
    }
}
