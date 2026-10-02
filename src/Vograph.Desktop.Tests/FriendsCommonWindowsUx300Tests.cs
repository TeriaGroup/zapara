using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class FriendsCommonWindowsUx300Tests : UiTest
{
    [Fact]
    public async Task Date_without_both_real_schedules_never_infers_free_time()
    {
        using var db = TestDb.Create();
        var vm = new FriendsViewModel(db.Services, new ShellViewModel(db.Services),
            () => new DateTime(2026, 9, 6, 12, 0, 0));
        await vm.LoadAsync();
        vm.SelectedComparisonFriend = Assert.Single(vm.Friends);
        vm.ComparisonDate = new DateTime(2026, 9, 6);

        await vm.CompareFreeIntervalsCommand.ExecuteAsync(null);

        Assert.Empty(vm.ComparisonWindows);
        Assert.Contains("нет занятий", vm.ComparisonStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Loaded_group_pair_uses_chosen_date_instead_of_parity_template()
    {
        using var db = TestDb.Create();
        var vm = new FriendsViewModel(db.Services, new ShellViewModel(db.Services),
            () => new DateTime(2026, 9, 6, 12, 0, 0));
        await vm.LoadAsync();
        vm.SelectedComparisonFriend = Assert.Single(vm.Friends);
        vm.ComparisonDate = new DateTime(2026, 9, 14);

        await vm.CompareFreeIntervalsCommand.ExecuteAsync(null);

        Assert.Contains("общ", vm.ComparisonStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("нет занятий", vm.ComparisonStatus, StringComparison.OrdinalIgnoreCase);
    }
}
