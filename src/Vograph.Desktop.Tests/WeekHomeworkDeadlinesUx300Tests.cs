using Vograph.Desktop.Features.Homeworks;
using Vograph.Core.Models;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekHomeworkDeadlinesUx300Tests
{
    [Fact]
    public async Task Actual_week_shows_exact_personal_deadline_and_opens_that_task()
    {
        using var db = TestDb.Create();
        var task = Assert.Single(db.Services.Homework.GetAll());
        Assert.NotNull(task.DueDateComputed);
        var shell = new ShellViewModel(db.Services);
        var week = new WeekViewModel(db.Services, shell, () => task.DueDateComputed!.Value.Date);
        await week.ReloadAsync();
        var deadline = Assert.Single(week.Days.SelectMany(day => day.Deadlines));
        Assert.Equal(task.Id, deadline.Id);

        deadline.OpenCommand.Execute(null);
        await Waits.Until(() => shell.CurrentKey == SectionKey.Homework, "personal homework deadline jump");
        var homework = shell.Section<HomeworkViewModel>(SectionKey.Homework);
        await Waits.Until(() => homework.Groups.SelectMany(group => group.Items).Any(row => row.Entry.Homework.Id == task.Id), "target homework");
        Assert.Equal(task.Id, homework.HighlightHomeworkId);
    }

    [Fact]
    public async Task Active_deadline_uses_current_schedule_projection_and_completed_keeps_historic_date()
    {
        using var db = TestDb.Create();
        var created = new DateTime(2026, 9, 5);
        var subject = db.Services.Homework.GetAll().Single().SubjectRawNormalized;
        (int Nth, DateTime Old, DateTime Insert)? change = null;
        for (var nth = 2; nth <= 10 && change is null; nth++)
        {
            var previous = db.Services.Homework.ComputeDueDate(subject, created, nth - 1);
            var old = db.Services.Homework.ComputeDueDate(subject, created, nth);
            if (previous is null || old is null) continue;
            var insert = Enumerable.Range(1, Math.Max(0, (old.Value.Date - previous.Value.Date).Days - 1))
                .Select(offset => previous.Value.Date.AddDays(offset))
                .FirstOrDefault(day => day.DayOfWeek != DayOfWeek.Sunday);
            if (insert != default) change = (nth, old.Value.Date, insert);
        }
        var chosen = Assert.IsType<(int Nth, DateTime Old, DateTime Insert)>(change);
        var taskId = db.Services.Homework.AddHomework(TestDb.MathSubject, "Проекция подгруппы", chosen.Nth, createdAt: created);
        Assert.Equal(chosen.Old, db.Services.Homework.GetById(taskId)!.DueDateComputed!.Value.Date);
        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = (int)chosen.Insert.DayOfWeek,
            Parity = 0, Index = 98, TimeStart = "07:00", TimeEnd = "08:30", SubjectRaw = TestDb.MathSubject,
            SubjectNormalized = subject, TypeRaw = "лек", TeacherRaw = "Петров", ClassroomRaw = "100;" });
        Assert.Equal(chosen.Insert, db.Services.Homework.ComputeDueDate(subject, created, chosen.Nth)!.Value.Date);
        var shell = new ShellViewModel(db.Services);
        var week = new WeekViewModel(db.Services, shell, () => chosen.Insert);
        await week.ReloadAsync();
        var deadline = Assert.Single(week.Days.SelectMany(day => day.Deadlines).Where(row => row.Id == taskId));
        Assert.Equal(chosen.Insert, deadline.Deadline.Due);
        await deadline.OpenCommand.ExecuteAsync(null);
        Assert.Equal(SectionKey.Homework, shell.CurrentKey);

        db.Services.Homework.MarkDone(taskId, true);
        var historic = new WeekViewModel(db.Services, shell, () => chosen.Old);
        await historic.ReloadAsync();
        Assert.Equal(chosen.Old, Assert.Single(historic.Days.SelectMany(day => day.Deadlines)
            .Where(row => row.Id == taskId)).Deadline.Due);
    }
}
