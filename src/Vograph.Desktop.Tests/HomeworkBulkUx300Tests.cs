using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkBulkUx300Tests : UiTest
{
    [Fact]
    public async Task Selected_personal_tasks_require_preview_and_only_successful_changes_are_undoable()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework("пр ИСТОРИЯ", "Доклад", 1, createdAt: new DateTime(2026, 9, 5));
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        vm.BulkMode = true;
        var rows = vm.Groups.SelectMany(group => group.Items).ToArray();
        Assert.Equal(2, rows.Length);
        foreach (var row in rows) row.SelectedForBulk = true;

        vm.RequestBulkCompletionCommand.Execute(null);
        Assert.True(vm.ShowBulkPreview);
        Assert.Contains("Доклад", vm.BulkPreviewText);
        Assert.All(db.Services.Homework.GetAll(), item => Assert.NotEqual("done", item.Status));

        await vm.ConfirmBulkCompletionCommand.ExecuteAsync(null);
        Assert.All(db.Services.Homework.GetAll(), item => Assert.Equal("done", item.Status));
        Assert.True(vm.HasBulkUndo);
        await vm.UndoBulkCompletionCommand.ExecuteAsync(null);
        Assert.All(db.Services.Homework.GetAll(), item => Assert.NotEqual("done", item.Status));
    }

    [Fact]
    public async Task Partial_stale_selection_reports_failure_and_undo_touches_only_successes()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework("пр ИСТОРИЯ", "Доклад", 1, createdAt: new DateTime(2026, 9, 5));
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        vm.BulkMode = true;
        var rows = vm.Groups.SelectMany(group => group.Items).ToArray();
        foreach (var row in rows) row.SelectedForBulk = true;
        vm.RequestBulkCompletionCommand.Execute(null);
        var staleId = rows[1].Entry.Homework.Id;
        db.Services.Homework.MarkDone(staleId, true);

        await vm.ConfirmBulkCompletionCommand.ExecuteAsync(null);
        Assert.Contains("Отмечено готово: 1; не удалось: 1", vm.BulkFeedback);
        await vm.UndoBulkCompletionCommand.ExecuteAsync(null);

        Assert.Equal("done", db.Services.Homework.GetById(staleId)!.Status);
        Assert.NotEqual("done", db.Services.Homework.GetById(rows[0].Entry.Homework.Id)!.Status);
    }
}
