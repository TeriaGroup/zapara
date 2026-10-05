using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class FriendGroupChoiceUx300Tests : UiTest
{
    [Fact]
    public async Task Unknown_edited_group_is_rejected_when_local_catalog_has_choices()
    {
        using var db = TestDb.Create();
        var vm = new FriendsViewModel(db.Services, new ShellViewModel(db.Services));
        await vm.LoadAsync();
        var item = Assert.Single(vm.Friends);
        item.BeginEditCommand.Execute(null);
        item.DraftGroupName = "НЕИЗВЕСТНАЯ ГРУППА";

        await item.SaveDraftCommand.ExecuteAsync(null);

        Assert.True(item.IsEditing);
        Assert.Contains("не найдена", item.DraftError);
        Assert.Equal("09С31", db.Services.Db.GetFriends().Single().GroupName);
    }
}
