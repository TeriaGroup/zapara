using Vograph.Core.Models;
using Vograph.Desktop.Dialogs;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupPickerTests
{
    private static readonly List<Group> Groups = new()
    {
        new Group { Id = "3313", Name = "А863С" },
        new Group { Id = "3031", Name = "09С31" },
        new Group { Id = "9999", Name = "Е452Б" },
        new Group { Id = "1", Name = "О3313" },
    };

    [Theory]
    [InlineData("A863", "А863С", true)]    // Latin A typed for Cyrillic А
    [InlineData("а863", "А863С", true)]    // lower-case Cyrillic
    [InlineData("09c", "09С31", true)]     // Latin c for Cyrillic С
    [InlineData("3313", "О3313", true)]
    [InlineData("3313", "А863С", false)]
    public void Matches_Ignores_Case_And_Latin_Lookalikes(string query, string name, bool expected) =>
        Assert.Equal(expected, GroupSearch.Matches(name, query));

    [Fact]
    public void Filter_Follows_Query_And_Preselects_Current_Group()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        Xunit.Assert.NotNull(db); // fixture only initializes Loc.Current

        var vm = new GroupPickerDialogViewModel(Groups, currentId: "3313");
        Assert.Equal(4, vm.Filtered.Count);
        Assert.Equal("3313", vm.Selected!.Id);
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.Query = "e45"; // Latin e → Е
        Assert.Single(vm.Filtered);
        Assert.Equal("Е452Б", vm.Filtered[0].Name);

        vm.Selected = null;
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Groups_Are_Sorted_By_Name()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new GroupPickerDialogViewModel(Groups, null);
        Assert.Equal(new[] { "09С31", "А863С", "Е452Б", "О3313" }, vm.Filtered.Select(g => g.Name));
    }
}
