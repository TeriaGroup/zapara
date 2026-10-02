using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkExpandUx300Tests : UiTest
{
    [Fact]
    public async Task Collapse_and_expand_all_survive_search_rebuild()
    {
        using var db = TestDb.Create();
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();
        Assert.False(Assert.Single(vm.Groups).IsCollapsed);

        vm.CollapseAllGroupsCommand.Execute(null);
        Assert.True(Assert.Single(vm.Groups).IsCollapsed);
        vm.SearchQuery = "§5";
        Assert.True(Assert.Single(vm.Groups).IsCollapsed);
        vm.ExpandAllGroupsCommand.Execute(null);
        Assert.False(Assert.Single(vm.Groups).IsCollapsed);
    }
}
