using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkFilteredPlanUx300Tests : UiTest
{
    [Fact]
    public void Transfer_text_uses_unknown_due_and_attachment_names_only()
    {
        var text = HomeworkTransferText.Format([new("Физика", "Задача", null, false, ["Фото.jpg"])]);

        Assert.Contains("без срока", text);
        Assert.Contains("Файлы: Фото.jpg", text);
        Assert.DoesNotContain("C:\\", text);
    }

    [Fact]
    public async Task Preview_contains_only_current_filtered_personal_tasks()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework("пр ИСТОРИЯ", "Доклад", 1, createdAt: new DateTime(2026, 9, 5));
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        vm.SearchQuery = "§5";

        vm.PreviewFilteredPlanCommand.Execute(null);

        Assert.Contains("§5", vm.FilteredPlanPreview);
        Assert.DoesNotContain("Доклад", vm.FilteredPlanPreview);
        Assert.True(vm.ShowFilteredPlanPreview);
    }
}
