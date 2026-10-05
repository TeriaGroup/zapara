using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkSubjectOverviewUx300Tests : UiTest
{
    [Fact]
    public async Task Overview_counts_all_personal_tasks_and_drills_to_exact_subject()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework("Физика", "Лабораторная", 1, createdAt: new DateTime(2026, 9, 5));
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        vm.SearchQuery = "ничего";

        Assert.Equal(2, vm.SubjectOverview.Count);
        var math = vm.SubjectOverview.Single(row => row.SubjectKey == db.Services.Homework.GetAll().Single(task => task.Text != "Лабораторная").SubjectRawNormalized);
        Assert.Equal(1, math.Active);
        math.OpenCommand.Execute(null);

        Assert.Equal(1, vm.Groups.Sum(group => group.Items.Count));
        Assert.All(vm.Groups.SelectMany(group => group.Items), row => Assert.Equal(math.SubjectKey, row.Entry.Homework.SubjectRawNormalized));
    }
}
