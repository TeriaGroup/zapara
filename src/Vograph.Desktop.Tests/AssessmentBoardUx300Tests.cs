using Vograph.Core.Models;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class AssessmentBoardUx300Tests : UiTest
{
    [Fact]
    public void Twenty_eight_actual_days_include_year_boundary_and_exclude_day_twenty_nine()
    {
        var group = TestDb.MyGroupId;
        var start = new DateTime(2026, 12, 20);
        var lesson = new Lesson { GroupId = group, SubjectRaw = "экз Математика", TypeRaw = "экз",
            TimeStart = "09:00", TimeEnd = "10:30", TeacherRaw = "Петров", ClassroomRaw = "101;" };
        var last = new Lesson { GroupId = group, SubjectRaw = "зач Физика", TypeRaw = "зач",
            TimeStart = "10:00", TimeEnd = "11:30", TeacherRaw = "Сидоров", ClassroomRaw = "102;" };
        var result = AssessmentPlanner.Create(group, start, start.AddDays(2), day =>
            day == start.AddDays(2) ? [lesson] :
            day == start.AddDays(27) ? [lesson, last] :
            day == start.AddDays(28) ? [last] : []);
        Assert.Equal(2, result.UnknownDays);
        Assert.Equal([start.AddDays(2), start.AddDays(27)], result.Items.Select(item => item.Date));
        Assert.Equal("Экзамен", AssessmentPlanner.Kind("экз"));
        Assert.Equal("Зачёт", AssessmentPlanner.Kind("", "зач ФИЗИКА"));
        Assert.Null(AssessmentPlanner.Kind("лекция"));
        Assert.Null(AssessmentPlanner.Kind("экзотика"));
        Assert.Null(AssessmentPlanner.Kind("зачеркнуто"));
        Assert.Null(AssessmentPlanner.Kind("", "экзаменатор по физике"));
        Assert.Equal("Зачёт", AssessmentPlanner.Kind("", "диф. зачёт ФИЗИКА"));
    }

    [Fact]
    public void Times_are_sorted_numerically_and_raw_slots_remain_distinct()
    {
        var group = TestDb.MyGroupId;
        var start = new DateTime(2026, 10, 5);
        Lesson Row(string time, string room) => new() { GroupId = group, DayOfWeek = 1,
            SubjectRaw = "Математика", TypeRaw = "экз", TimeStart = time, TimeEnd = "11:30",
            ClassroomRaw = room };
        var result = AssessmentPlanner.Create(group, start, start, day =>
            day == start ? [Row("10:00", "101;"), Row("9:00", "102;")] :
            day == start.AddDays(7) ? [Row("10:00", "101;")] : []);
        Assert.Equal(["9:00", "10:00"], result.Items.Select(item => item.TimeStart));
        Assert.Equal(["102;", "101;"], result.Items.Select(item => item.ClassroomRaw));
    }

    [Fact]
    public async Task Assessment_card_opens_exact_raw_lesson_of_own_group()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 0,
            Index = 95, TimeStart = "17:00", TimeEnd = "18:30", SubjectRaw = "экз ТЕСТ UX300",
            SubjectNormalized = "экз тест ux300", TypeRaw = "экз", TeacherRaw = "Петров",
            ClassroomRaw = "101;" });
        var shell = new ShellViewModel(db.Services);
        var week = new WeekViewModel(db.Services, shell, () => new DateTime(2026, 9, 14));
        await week.ReloadAsync();
        await week.ShowAssessmentsCommand.ExecuteAsync(null);
        var exam = Assert.Single(week.Assessments, row => row.Entry.SubjectRaw == "экз ТЕСТ UX300" &&
            row.Entry.Date == new DateTime(2026, 9, 14));
        await exam.OpenCommand.ExecuteAsync(null);
        Assert.Equal(SectionKey.Schedule, shell.CurrentKey);
        Assert.Equal("экз", exam.Entry.TypeRaw);
    }

    [Fact]
    public async Task Ambiguous_identical_raw_lessons_do_not_focus_an_arbitrary_row()
    {
        using var db = TestDb.Create();
        for (var index = 95; index <= 96; index++)
            db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1,
                Parity = 0, Index = index, TimeStart = "17:00", TimeEnd = "18:30",
                SubjectRaw = "экз ДУБЛЬ", SubjectNormalized = "экз дубль", TypeRaw = "экз",
                TeacherRaw = "Петров", ClassroomRaw = "101;" });
        var shell = new ShellViewModel(db.Services);
        var week = new WeekViewModel(db.Services, shell, () => new DateTime(2026, 9, 14));
        await week.ReloadAsync();
        await week.ShowAssessmentsCommand.ExecuteAsync(null);
        var candidates = week.Assessments.Where(row => row.Entry.SubjectRaw == "экз ДУБЛЬ" &&
            row.Entry.Date == new DateTime(2026, 9, 14)).ToArray();
        Assert.Equal(2, candidates.Length);
        var before = shell.CurrentKey;
        await candidates[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal(before, shell.CurrentKey);
    }
}
