using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Dialogs;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Adoption_unfetched_selection_failure_is_not_presented_as_known_empty()
    {
        var fail = false;
        using var handler = new FakeHttpHandler { Respond = _ => fail ? Text("{}") : Json(Catalog()) };
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            var settings = app.Db.GetSettings(); settings.MyGroupId = "a"; settings.AutoUpdate = false; app.Db.SaveSettings(settings);
            fail = true;
            var shell = new ShellViewModel(app);
            try
            {
                await shell.StartAsync();
                Assert.IsType<Vograph.Desktop.Features.States.ErrorStateViewModel>(shell.Current);
            }
            finally { shell.Stop(); }
        }
        finally { CleanupApiDirectory(dir); }
    }

    [Fact]
    public async Task Adoption_shell_bootstrap_picker_and_server_stale_use_api_not_xml()
    {
        using var handler = new FakeHttpHandler { Respond = r =>
        {
            var json = r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog() : Schedule();
            json["meta"]!["stale"] = true;
            return Json(json);
        }};
        using var legacy = new FakeHttpHandler { Respond = _ => throw new HttpRequestException("legacy forbidden") };
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            var settings = app.Db.GetSettings();
            settings.AutoUpdate = false;
            app.Db.SaveSettings(settings);
            app.Refresher = new ScheduleRefresher(legacy);
            var shell = new ShellViewModel(app);
            try
            {
                await shell.StartAsync();
                Assert.IsType<ScheduleViewModel>(shell.Current);
                var pick = shell.OpenGroupPickerCommand.ExecuteAsync(null);
                var dialog = await Waits.ForDialogAsync<GroupPickerDialogViewModel>(shell);
                dialog.Selected = dialog.Filtered.Single(g => g.Id == "a");
                dialog.ConfirmCommand.Execute(null);
                await pick;
                Assert.Single(app.Db.GetAllLessonsForGroup("a"));
                Assert.True(shell.StaleWarn);
                Assert.Empty(legacy.Requests);
            }
            finally { shell.Stop(); }
        }
        finally { CleanupApiDirectory(dir); }
    }
}
