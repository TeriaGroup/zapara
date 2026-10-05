using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Friends;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Adoption_friend_add_and_enable_load_missing_cache_before_preview()
    {
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            var catalog = r.RequestUri!.AbsolutePath.EndsWith("/groups");
            var json = catalog ? Catalog() : Schedule(r.RequestUri.AbsolutePath.Contains("/empty/") ? "empty" : "a");
            if (catalog) json["groups"]![1]!["name"] = "Friend";
            else if (json["group"]!["id"]!.GetValue<string>() == "empty") json["group"]!["name"] = "Friend";
            return Json(json);
        }};
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var settings = app.Db.GetSettings(); settings.MyGroupId = "a"; app.Db.SaveSettings(settings);
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            var shell = new ShellViewModel(app);
            var vm = new FriendsViewModel(app, shell);
            try
            {
                await vm.LoadAsync();
                var add = vm.AddCommand.ExecuteAsync(null);
                var dialog = await Waits.ForDialogAsync<GroupPickerDialogViewModel>(shell);
                dialog.Selected = Assert.Single(dialog.Filtered);
                dialog.ConfirmCommand.Execute(null);
                await add;
                Assert.NotNull(new TimetableApiCache(app.Db).Read("empty")?.Meta);
                var friend = Assert.Single(vm.Friends);
                var disabled = app.Db.GetFriends().Single(); disabled.Enabled = false;
                await app.CoreGate.WaitAsync(TestContext.Current.CancellationToken);
                try
                {
                    app.Db.UpdateFriend(disabled);
                    using var cmd = app.Db.Connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM api_cache_metadata WHERE groupId='empty'"; cmd.ExecuteNonQuery();
                }
                finally { app.CoreGate.Release(); }
                friend.ApplyModel(disabled);
                friend.Enabled = true;
                await vm.SaveAsync(friend); // await an explicit save as well as the UI property's asynchronous save
                Assert.NotNull(new TimetableApiCache(app.Db).Read("empty")?.Meta);
                Assert.True(app.Db.GetFriends().Single().Enabled);
                await vm.RefreshPreviewAsync();
            }
            finally { vm.Detach(); shell.Stop(); }
        }
        finally { CleanupApiDirectory(dir); }
    }
}
