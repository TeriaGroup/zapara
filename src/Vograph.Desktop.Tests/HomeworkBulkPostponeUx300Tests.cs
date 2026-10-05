using Vograph.Desktop.Features.Homeworks;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Microsoft.Data.Sqlite;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkBulkPostponeUx300Tests : UiTest
{
    [Fact]
    public async Task Preview_postpones_one_occurrence_and_undo_only_if_applied_state_remains()
    {
        using var db = TestDb.Create();
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        var row = vm.Groups.SelectMany(group => group.Items).Single();
        var before = db.Services.Homework.GetById(row.Entry.Homework.Id)!;
        vm.BulkMode = true;
        row.SelectedForBulk = true;

        await vm.RequestBulkPostponeCommand.ExecuteAsync(null);
        Assert.True(vm.ShowPostponePreview);
        Assert.Equal(before.TargetNthOccurrence, db.Services.Homework.GetById(before.Id)!.TargetNthOccurrence);
        Assert.Contains("→", vm.PostponePreviewText);

        await vm.ConfirmBulkPostponeCommand.ExecuteAsync(null);
        Assert.Equal(before.TargetNthOccurrence + 1, db.Services.Homework.GetById(before.Id)!.TargetNthOccurrence);
        Assert.True(vm.HasPostponeUndo);
        await vm.UndoBulkPostponeCommand.ExecuteAsync(null);
        Assert.Equal(before.TargetNthOccurrence, db.Services.Homework.GetById(before.Id)!.TargetNthOccurrence);
    }

    [Fact]
    public async Task Stale_personal_task_is_skipped_and_retry_previews_only_failure()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework(TestDb.MathSubject, "Другой вариант", 1,
            createdAt: new DateTime(2026, 9, 5));
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        vm.BulkMode = true;
        var rows = vm.Groups.SelectMany(group => group.Items).ToArray();
        Assert.Equal(2, rows.Length);
        foreach (var row in rows) row.SelectedForBulk = true;
        await vm.RequestBulkPostponeCommand.ExecuteAsync(null);
        var stale = rows.Single(row => row.Text == "Другой вариант").Entry.Homework;
        db.Services.Homework.UpdateHomework(stale.Id, "Внешняя правка", stale.TargetNthOccurrence);

        await vm.ConfirmBulkPostponeCommand.ExecuteAsync(null);

        Assert.Contains("Перенесено: 1; не удалось: 1", vm.PostponeFeedback);
        vm.RetryFailedPostponeCommand.Execute(null);
        Assert.Contains("Другой вариант", vm.PostponePreviewText);
        Assert.DoesNotContain("§5", vm.PostponePreviewText);
        await vm.UndoBulkPostponeCommand.ExecuteAsync(null);
        Assert.Equal("Внешняя правка", db.Services.Homework.GetById(stale.Id)!.Text);
    }

    [Fact]
    public async Task Undo_skips_when_old_occurrence_moves_but_applied_occurrence_is_unchanged()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        const string subject = "лек UX300 ПЕРЕНОС";
        foreach (var (dow, index) in new[] { (1, 91), (3, 92) })
            db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = dow, Parity = 0,
                Index = index, TimeStart = "09:00", TimeEnd = "10:30", SubjectRaw = subject,
                SubjectNormalized = ParityService.NormalizeSubject(subject), TeacherRaw = "Тестовый", ClassroomRaw = "100;" });
        var id = db.Services.Homework.AddHomework(subject, "Вернуть только тот же срок", 1,
            createdAt: new DateTime(2026, 9, 6));
        Assert.Equal(new DateTime(2026, 9, 7), db.Services.Homework.GetById(id)!.DueDateComputed!.Value.Date);
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync(); vm.BulkMode = true;
        Assert.Single(vm.Groups.SelectMany(group => group.Items)).SelectedForBulk = true;
        await vm.RequestBulkPostponeCommand.ExecuteAsync(null);
        await vm.ConfirmBulkPostponeCommand.ExecuteAsync(null);
        Assert.Equal(new DateTime(2026, 9, 9), db.Services.Homework.GetById(id)!.DueDateComputed!.Value.Date);

        var backup = Path.Combine(db.Dir, "before-test-schedule-move.db");
        using (var copy = new SqliteConnection($"Data Source={backup}"))
        { copy.Open(); db.Services.Db.Connection.BackupDatabase(copy); copy.Close(); SqliteConnection.ClearPool(copy); }
        Assert.True(new FileInfo(backup).Length > 4096);
        using (var command = db.Services.Db.Connection.CreateCommand())
        {
            command.CommandText = "UPDATE schedule_cache SET dayOfWeek=2 WHERE groupId=@group AND subjectRaw=@subject AND idx=91 AND dayOfWeek=1";
            command.Parameters.AddWithValue("@group", TestDb.MyGroupId);
            command.Parameters.AddWithValue("@subject", subject);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        var current = db.Services.Homework.GetById(id)!;
        Assert.Equal(new DateTime(2026, 9, 8), db.Services.Homework.ComputeDueDate(current.SubjectRawNormalized, current.CreatedAt, 1)!.Value.Date);
        Assert.Equal(new DateTime(2026, 9, 9), db.Services.Homework.ComputeDueDate(current.SubjectRawNormalized, current.CreatedAt, 2)!.Value.Date);

        await vm.UndoBulkPostponeCommand.ExecuteAsync(null);

        var unchanged = db.Services.Homework.GetById(id)!;
        Assert.Equal(2, unchanged.TargetNthOccurrence);
        Assert.Equal(new DateTime(2026, 9, 9), unchanged.DueDateComputed!.Value.Date);
        Assert.Contains("пропущено после других изменений: 1", vm.PostponeFeedback);
    }
}
