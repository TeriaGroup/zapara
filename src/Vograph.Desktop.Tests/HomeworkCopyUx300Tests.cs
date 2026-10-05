using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkCopyUx300Tests : UiTest
{
    [Fact]
    public async Task Create_similar_starts_new_unfinished_local_task_without_copying_files()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new HomeworkViewModel(db.Services, shell, () => new DateTime(2026, 9, 14));
        await vm.LoadAsync();
        var original = vm.Groups.SelectMany(group => group.Items).Single();
        await vm.ToggleDoneAsync(original);
        vm.StatusFilter = 1;
        var done = vm.Groups.SelectMany(group => group.Items).Single();
        var oldId = done.Entry.Homework.Id;

        var creating = vm.CreateSimilarAsync(done);
        var dialog = await Waits.ForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.False(dialog.IsEdit);
        Assert.Equal(done.Text, dialog.Text);
        Assert.Empty(dialog.Files);
        dialog.ConfirmCommand.Execute(null);
        await creating;

        var all = db.Services.Homework.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, item => item.Id != oldId && item.Status != "done");
    }
}
