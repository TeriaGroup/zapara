using Vograph.Desktop.Controls;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Schedule;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleComposerTests
{
    // Fixture calendar: week containing 1 Sep is week 1 odd (Mon–Sun). Mon 07.09 is even; Mon 14.09 is odd.
    private static readonly DateTime MonMorning = new(2026, 9, 7, 8, 0, 0);
    private static readonly DateTime OddMondayMorning = new(2026, 9, 14, 8, 0, 0);

    [Fact]
    public void Monday_Morning_Has_Two_Rows_With_Override_Homework_And_Friends()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(0, OddMondayMorning);

        Assert.Equal("Сегодня", day.Title);
        Assert.Equal("Понедельник, 14 сентября · нечётная неделя · неделя 3 · 2 пары", day.Subtitle);
        Assert.Equal(2, day.Rows.Count);

        var math = day.Rows[0];
        Assert.Equal("09:00", math.TimeStart);
        Assert.Equal("10:35", math.TimeEnd);
        Assert.Equal("Матан", math.DisplayName);
        Assert.Equal("Высшая математика", math.OriginalName);
        Assert.Equal("лекции — в 493", math.Note);
        Assert.Equal("Лекция", math.TypeLabel);
        Assert.Equal("Барт Е. Л.", math.Teacher);
        Assert.Equal("493", math.RoomText);
        Assert.Equal("ГК", math.BuildingTag);
        Assert.Equal("след. 21.09", math.NextDateText); // next odd-week Monday lecture after 14.09
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
        Assert.Equal("Основы российской государственности", law.DisplayName);
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
        var day = new ScheduleComposer(db.Services).Compose(0, new DateTime(2026, 9, 14, 11, 0, 0));
        Assert.True(day.Rows[0].IsPast);
        Assert.False(day.Rows[0].IsNext);
        Assert.True(day.Rows[1].IsNext);
    }

    [Fact]
    public void Sunday_Is_Empty_And_Offers_The_Next_Known_Study_Day()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(-1, MonMorning);
        Assert.Empty(day.Rows);
        Assert.Equal("Вчера", day.Title);
        Assert.Equal("Воскресенье — пар нет", day.EmptyTitle);
        Assert.NotNull(day.EmptyHint);
        Assert.NotNull(day.NextStudyDate);
    }

    [Fact]
    public void Empty_Weekday_Hints_At_Next_Lesson()
    {
        using var db = TestDb.Create();
        var day = new ScheduleComposer(db.Services).Compose(1, MonMorning); // Tue 08.09, even: no lessons
        Assert.Empty(day.Rows);
        Assert.Equal("Пар нет", day.EmptyTitle);
        Assert.Equal("Следующая пара: ср, 9 сент., 14:55", day.EmptyHint);
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
        var day = new ScheduleComposer(db.Services).Compose(0, OddMondayMorning.AddHours(6));
        Assert.Single(day.Rows[0].Friends);   // past: same room → shown
        Assert.Empty(day.Rows[1].Friends);    // past: same building only → hidden

        s.AlwaysShowAllTrafficLights = true;
        db.Services.Db.SaveSettings(s);
        day = new ScheduleComposer(db.Services).Compose(0, OddMondayMorning.AddHours(6));
        Assert.Equal(DotFill.Off, Assert.Single(day.Rows[1].Friends).Fill);
    }

    [Fact]
    public void Every_Future_Lesson_Has_Status_And_Threshold_Does_Not_Hide_Friends()
    {
        using var db = TestDb.Create();
        var settings = db.Services.Db.GetSettings();
        settings.IntersectionStrictness = 100;
        db.Services.Db.SaveSettings(settings);
        var composer = new ScheduleComposer(db.Services);
        var day = composer.Compose(0, OddMondayMorning);
        Assert.All(day.Rows, row => Assert.True(row.IsUpcoming));
        var below = Assert.Single(day.Rows[1].Friends);
        Assert.True(below.HasLesson);
        Assert.True(below.ShowLessonStatus);
        Assert.Equal(DotFill.Off, below.Fill);
        Assert.Equal("Пара в это время", new FriendMarkViewModel(below).LessonStatusCaption);
        Assert.False(composer.Compose(0, OddMondayMorning.AddHours(1)).Rows[0].IsUpcoming);
        Assert.False(composer.Compose(0, OddMondayMorning.AddHours(2)).Rows[0].IsUpcoming);
        Assert.All(composer.Compose(-7, OddMondayMorning).Rows, row => Assert.False(row.IsUpcoming));
        Assert.All(composer.Compose(7, OddMondayMorning).Rows, row => Assert.True(row.IsUpcoming));
    }

    [Fact]
    public void Future_Friend_Status_Distinguishes_Missing_Schedule_And_No_Overlap()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertFriend(new Vograph.Core.Models.FriendGroup { GroupName = "Нет в каталоге", Enabled = true });
        db.Services.Db.InsertFriend(new Vograph.Core.Models.FriendGroup { GroupName = "А863С", Enabled = true });
        var day = new ScheduleComposer(db.Services).Compose(2, OddMondayMorning);
        var row = Assert.Single(day.Rows);
        var unknown = Assert.Single(row.Friends.Where(mark => mark.GroupName == "Нет в каталоге"));
        Assert.Null(unknown.HasLesson);
        Assert.Equal("Нет данных", new FriendMarkViewModel(unknown).LessonStatusCaption);
        var known = Assert.Single(row.Friends.Where(mark => mark.GroupName == "09С31"));
        Assert.False(known.HasLesson);
        Assert.Equal("Нет пары в это время", new FriendMarkViewModel(known).LessonStatusCaption);
        Assert.DoesNotContain(row.Friends, mark => mark.GroupName == "А863С");
    }

    [Theory]
    [InlineData("10:30", "12:05", true)]
    [InlineData("10:35", "12:10", false)]
    [InlineData("07:25", "09:00", false)]
    [InlineData("09:00", "", true)]
    [InlineData("09:00", "bad", null)]
    [InlineData("09:00", "08:00", null)]
    [InlineData("bad", "10:00", null)]
    public void Friend_Status_Uses_Real_Overlap_And_Unknown_For_Invalid_Intervals(string start, string end, bool? expected)
    {
        var mine = new Vograph.Core.Models.Lesson { TimeStart = "09:00", TimeEnd = "10:35" };
        var other = new Vograph.Core.Models.Lesson { TimeStart = start, TimeEnd = end };
        Assert.Equal(expected, FriendMarks.HasLesson(mine, new[] { other }));
        Assert.True(FriendMarks.HasLesson(mine, new[] { other, mine }));
    }

    [Theory]
    [InlineData("493", "ГК", 100)]
    [InlineData("412", "ВЦ", 75)]
    [InlineData("212", "ГК", 50)]
    [InlineData("493", "УЛК", 25)]
    public void Future_Lesson_Status_Is_Independent_Of_Place_Score(string room, string building, int expectedScore)
    {
        using var db = TestDb.Create();
        var mine = new Vograph.Core.Models.Lesson { TimeStart = "09:00", TimeEnd = "10:35", RoomRaw = "493", BuildingRaw = "ГК" };
        var other = new Vograph.Core.Models.Lesson { TimeStart = "09:00", TimeEnd = "10:35", RoomRaw = room, BuildingRaw = building };
        var friend = new Vograph.Core.Models.FriendGroup { GroupName = "09С31", Enabled = true };
        var schedules = new[] { new FriendMarks.DaySchedule(friend, new[] { other }) };
        var settings = new Vograph.Core.Models.Settings { IntersectionStrictness = 100 };
        Assert.Equal(expectedScore, Vograph.Core.Services.IntersectionService.PlaceScore(mine, other));
        var mark = Assert.Single(FriendMarks.Compute(mine, schedules, settings, db.Services.Loc, upcoming: true));
        Assert.True(mark.HasLesson);
        Assert.Equal(expectedScore == 100 ? DotFill.Full : DotFill.Off, mark.Fill);
    }

    [Fact]
    public void Invalid_Start_Is_Never_Upcoming_And_Clock_Update_Removes_Status()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertLesson(new Vograph.Core.Models.Lesson
        {
            GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 1, TimeStart = "bad", TimeEnd = "10:35", SubjectRaw = "Invalid"
        });
        var composer = new ScheduleComposer(db.Services);
        var morning = composer.Compose(0, OddMondayMorning);
        Assert.False(Assert.Single(morning.Rows, row => row.DisplayName == "Invalid").IsUpcoming);
        var shell = new Vograph.Desktop.Shell.ShellViewModel(db.Services);
        var owner = new ScheduleViewModel(db.Services, shell);
        var original = Assert.Single(morning.Rows, row => row.TimeStart == "09:00");
        var vm = new LessonRowViewModel(original, owner, 0);
        Assert.True(vm.IsUpcoming);
        vm.Update(Assert.Single(composer.Compose(0, OddMondayMorning.AddHours(1)).Rows, row => row.TimeStart == "09:00"));
        Assert.False(vm.IsUpcoming);
        Assert.All(vm.Friends, mark => Assert.False(mark.ShowLessonStatus));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Future_Friend_With_Stale_Or_Incompatible_Copy_Is_Unknown(bool stale, bool differentSnapshot)
    {
        using var db = TestDb.Create();
        var snapshot = Guid.NewGuid();
        foreach (var groupId in new[] { TestDb.MyGroupId, "3031" })
        {
            var friend = groupId == "3031";
            var metadata = new Vograph.Core.Services.CacheMetadata(
                new(new DateOnly(2026, 9, 1), 2, "Semester", "Europe/Moscow"),
                new(friend && differentSnapshot ? Guid.NewGuid() : snapshot, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    null, "xml", null, "fixture", friend && stale), "2026-09-14T07:00:00Z", "api", "fixture");
            using var command = db.Services.Db.Connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO api_cache_metadata(groupId, payload) VALUES (@id, @payload)";
            command.Parameters.AddWithValue("@id", groupId);
            command.Parameters.AddWithValue("@payload", System.Text.Json.JsonSerializer.Serialize(metadata));
            command.ExecuteNonQuery();
        }
        var composer = new ScheduleComposer(db.Services);
        var future = Assert.Single(composer.Compose(0, OddMondayMorning).Rows[0].Friends);
        Assert.Null(future.HasLesson);
        Assert.Equal(DotFill.Off, future.Fill);
        Assert.Equal("Нет данных", new FriendMarkViewModel(future).LessonStatusCaption);
        Assert.Empty(composer.Compose(0, OddMondayMorning.AddHours(1)).Rows[0].Friends);
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

    /// <summary>With no PeriodStart in settings the fallback is «1 September of the START date's year» for the whole
    /// search — not re-derived from every visited day. Re-anchoring per day would put Sept 2027 after Jan 2027 and
    /// clamp every week code to 1, so the odd-week-only «пр ОСН РОС ГОС» would falsely land on the even Monday 04.01.</summary>
    [Fact]
    public void Next_Occurrence_Keeps_One_Period_Anchor_Across_New_Year()
    {
        using var db = TestDb.Create();
        var settings = db.Services.Db.GetSettings();
        settings.PeriodStart = null; // the fallback path
        // Mon 28.12.2026 is odd week 17 from Mon 31.08.2026; the next odd Monday is 04.01.2027 (week 19).
        Assert.Equal(new DateTime(2027, 1, 4), NextOccurrence.Find(db.Services.Db, settings, "пр ОСН РОС ГОС", new DateTime(2026, 12, 28)));
        // Two odd Mondays (04.01 and 18.01) lie strictly between 28.12 and 01.02.
        Assert.Equal(2, HomeworkLabels.LessonsUntil(db.Services.Db, settings, Vograph.Core.Services.ParityService.NormalizeSubject("пр ОСН РОС ГОС"), new DateTime(2026, 12, 28), new DateTime(2027, 2, 1)));
    }
}
