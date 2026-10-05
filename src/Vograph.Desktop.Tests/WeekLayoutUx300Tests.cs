using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekLayoutUx300Tests
{
    [Fact]
    public async Task Week_columns_follow_available_width_and_busy_state_finishes_after_load()
    {
        using var db = TestDb.Create();
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services));
        vm.SetViewportWidth(580); Assert.Equal(1, vm.WeekColumns);
        vm.SetViewportWidth(800); Assert.Equal(2, vm.WeekColumns);
        vm.SetViewportWidth(1200); Assert.Equal(3, vm.WeekColumns);
        await vm.ReloadAsync();
        Assert.False(vm.IsLoading);
        Assert.True(vm.IsLoaded);
    }
}
