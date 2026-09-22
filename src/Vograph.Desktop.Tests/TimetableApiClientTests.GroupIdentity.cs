using System.Text.Json.Nodes;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Core.Services.Sync;
using Zapara.Contracts.Sync;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_adoption_and_account_settings_outbox_share_the_same_transaction(bool failCommit)
    {
        using var handler = new FakeHttpHandler { Respond = r => IdentityResponse(r, new() { ["О3313"] = "О3313" }) };
        using var http = new HttpClient(handler); var dir = IdentityTestDirectory();
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            app.Db.UpsertGroup(new() { Id = "42", Name = "О3313" });
            var settings = app.Db.GetSettings(); settings.MyGroupId = "42"; app.Db.SaveSettings(settings);
            var box = new PrivateSyncOutbox(app.Db, enabled: true); app.Db.PrivateOutbox = box;
            if (failCommit) box.BeforeCommit = () => throw new IOException("disk full");
            Assert.Equal(!failCommit, await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            if (failCommit)
            {
                Assert.Equal("42", app.Db.GetSettings().MyGroupId); Assert.Empty(box.Pending());
                Assert.Null(new TimetableApiCache(app.Db).Read("")); Assert.Empty(app.Db.GetAllLessonsForGroup("О3313"));
            }
            else
            {
                var pending = Assert.Single(box.Pending()); Assert.Equal("settings", pending.EntityType);
                var value = Assert.IsType<SettingsValue>(box.BuildMutation(pending, Guid.NewGuid())!.Value);
                Assert.Equal("О3313", value.SelectedGroupId); Assert.Equal("О3313", app.Db.GetSettings().MyGroupId);
            }
        }
        finally { CleanupApiDirectory(dir); }
    }
    [Theory]
    [InlineData("42", "О3313", "77", "А4313")]
    [InlineData("О3313", "42", "А4313", "77")]
    public async Task Api_refresh_preserves_selected_group_and_friends_across_XML_JSON_identity_changes(string oldId, string newId, string oldFriend, string newFriend)
    {
        var names = new Dictionary<string, string> { [newId] = "О3313", [newFriend] = "А4313" };
        using var handler = new FakeHttpHandler { Respond = r => IdentityResponse(r, names) };
        using var http = new HttpClient(handler);
        var dir = IdentityTestDirectory();
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            app.Db.UpsertGroup(new() { Id = oldId, Name = "О3313" }); app.Db.UpsertGroup(new() { Id = oldFriend, Name = "А4313" });
            app.Db.InsertLesson(new() { GroupId = oldId, DayOfWeek = 1, Parity = 1, Index = 1, TimeStart = "09:00", TimeEnd = "10:35", SubjectRaw = "Старое расписание", SubjectNormalized = "старое расписание" });
            var settings = app.Db.GetSettings(); settings.MyGroupId = oldId; app.Db.SaveSettings(settings);
            app.Db.InsertFriend(new() { GroupName = "А4313", MemberNames = "Маша", ColorHex = "#4CC38A", Enabled = true });
            var homework = app.Homework.AddHomework("лек Ёлка Тест", "Сохранить задание", 1);
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Equal(newId, app.Db.GetSettings().MyGroupId);
            Assert.NotEmpty(app.Db.GetAllLessonsForGroup(newId));
            Assert.NotEmpty(app.Db.GetAllLessonsForGroup(newFriend));
            Assert.NotEmpty(app.Db.GetAllLessonsForGroup(oldId));
            Assert.Equal("Маша", Assert.Single(app.Db.GetFriends()).MemberNames);
            Assert.Equal("Сохранить задание", app.Homework.GetById(homework)!.Text);
        }
        finally { CleanupApiDirectory(dir); }
    }

    [Fact]
    public async Task Ambiguous_group_name_preserves_last_good_selection_and_cache()
    {
        using var handler = new FakeHttpHandler { Respond = r => IdentityResponse(r, new() { ["one"] = "О3313", ["two"] = "О3313" }) };
        using var http = new HttpClient(handler); var dir = IdentityTestDirectory();
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            app.Db.UpsertGroup(new() { Id = "42", Name = "О3313" });
            var settings = app.Db.GetSettings(); settings.MyGroupId = "42"; app.Db.SaveSettings(settings);
            Assert.False(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Equal("42", app.Db.GetSettings().MyGroupId);
            Assert.Null(new TimetableApiCache(app.Db).Read(""));
            Assert.Equal(TimetableApiFailure.UnknownRequiredGroup, app.Api.LastFailure);
        }
        finally { CleanupApiDirectory(dir); }
    }

    private static System.Net.Http.HttpResponseMessage IdentityResponse(System.Net.Http.HttpRequestMessage request, Dictionary<string, string> names)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/groups"))
        {
            var catalog = Catalog(Pin, names.Keys.ToArray());
            foreach (var group in catalog["groups"]!.AsArray()) group!["name"] = names[group["id"]!.GetValue<string>()];
            return Json(catalog);
        }
        var id = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Split('/')[^2]);
        var schedule = Schedule(id); schedule["group"]!["name"] = names[id];
        return Json(schedule);
    }
    private static string IdentityTestDirectory() => Path.Combine(Environment.GetEnvironmentVariable("ZAPARA_TEST_SCRATCH") ?? Path.GetTempPath(), "native-group-tests", Guid.NewGuid().ToString("N"));
}
