using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class SharedHomeworkInboxTests
{
    private static readonly Guid OtherCommunity = Guid.NewGuid();
    private static GroupHomeworkCopyResponse Copy(string title) => new(Guid.NewGuid(), title, "Задание без привязки к паре", 1, false, 0);

    private sealed class Fixture : IDisposable
    {
        // Keep the account DB plus its 64-character server key below SQLite's Windows path limit.
        private readonly string directory = CreateFixtureDirectory();
        private readonly AppServices guest;
        public readonly AppServices App;
        private readonly AccountClientHandler handler = new();
        private readonly HttpClient http;
        private readonly CommunityHttpClient client;
        public readonly HomeworkViewModel Vm;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Copies = _ => Task.FromResult(Payload(new[] { Copy("Проект") }));
        public bool TwoCommunities;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Complete = _ => Task.FromResult(Payload(Completion));
        public Fixture()
        {
            guest = AppServices.Create(directory, () => false);
            var profile = ProfileDescriptor.Account(directory, new string('A', 64), StaffId);
            Directory.CreateDirectory(Path.GetDirectoryName(profile.DatabasePath)!);
            App = guest.CreateProfile(profile);
            App.AllowNetwork = false;
            App.Parser.RefreshAsync(xmlOverride: File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"))).GetAwaiter().GetResult();
            SetGroup(TestDb.MyGroupId);
            http = new(handler); client = new(http, Root);
            App.UseCommunities(client, _ => Task.FromResult<string?>(Access));
            handler.Send = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/homework/copies")) return Copies(request);
                if (path.EndsWith("/completion")) return Complete(request);
                var currentGroup = request.RequestUri.Query.Contains("groupId=" + TestDb.MyGroupId, StringComparison.Ordinal);
                var community = currentGroup ? Membership : new CommunityResponse(OtherCommunity, "Другая группа", "Учебная группа", 1, "member");
                CommunityResponse[] found = currentGroup && TwoCommunities
                    ? [community, new CommunityResponse(OtherCommunity, "О3313-2", "Другое сообщество", 1, "member")]
                    : [community];
                return Task.FromResult(Payload(found));
            };
            Vm = new(App, new ShellViewModel(App));
        }
        public void SetGroup(string id) { var settings = App.Db.GetSettings(); settings.MyGroupId = id; App.Db.SaveSettings(settings); }
        public async Task Load()
        {
            await Vm.LoadAsync();
            await Waits.Until(() => Vm.SharedLoaded && !Vm.SharedLoading, "shared homework loaded");
        }
        private static string CreateFixtureDirectory()
        {
            var configured = Environment.GetEnvironmentVariable("VOGRAPH_TEST_DATA_ROOT");
            var basePath = Path.GetFullPath(configured is { Length: > 0 } ? configured : Path.GetTempPath());
            if (configured is { Length: > 0 } && !basePath.StartsWith(@"\\?\", StringComparison.Ordinal)) basePath = @"\\?\" + basePath;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var path = Path.Combine(basePath, Path.GetRandomFileName()[..2]);
                if (Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
                return path;
            }
            throw new IOException("No free test directory.");
        }
        public void Dispose()
        {
            Vm.Detach(); client.Dispose(); http.Dispose(); App.Dispose(); guest.Dispose();
            foreach (var path in Directory.EnumerateFiles(directory, "*.db", SearchOption.AllDirectories))
            { using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"); Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection); }
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task One_failed_community_keeps_successful_homework_and_names_partial_result()
    {
        using var f = new Fixture { TwoCommunities = true };
        f.Copies = request => Task.FromResult(request.RequestUri!.AbsolutePath.Contains(OtherCommunity.ToString("D"), StringComparison.OrdinalIgnoreCase)
            ? Problem(503, "db_unavailable") : Payload(new[] { Copy("Сохранённое задание") }));
        await f.Vm.LoadAsync();
        await Waits.Until(() => f.Vm.SharedLoaded && !f.Vm.SharedLoading, "partial shared homework");
        Assert.Equal("Сохранённое задание", Assert.Single(f.Vm.SharedTasks).Item.Title);
        Assert.Contains("Часть заданий", f.Vm.SharedFeedback);
    }

    [AvaloniaFact]
    public async Task Recipient_sees_assignments_without_a_deadline_in_the_homework_page()
    {
        using var f = new Fixture();
        await f.Load();
        Assert.True(f.Vm.ShowSharedTasks);
        Assert.Equal("Проект", Assert.Single(f.Vm.VisibleSharedTasks).Item.Title);
        Assert.Null(f.Vm.VisibleSharedTasks[0].Item.DeadlineAt);
        Assert.Empty(f.App.Homework.GetAll());
    }

    [AvaloniaFact]
    public async Task Shared_tasks_follow_the_same_status_and_search_filters_as_personal_tasks()
    {
        using var f = new Fixture();
        var done = new GroupHomeworkCopyResponse(Guid.NewGuid(), "Математика", "Групповой доклад", 1, true, 1);
        var open = new GroupHomeworkCopyResponse(Guid.NewGuid(), "Физика", "Решить задачи", 1, false, 0);
        f.Copies = _ => Task.FromResult(Payload(new[] { done, open }));
        await f.Load();

        Assert.Equal("Физика", Assert.Single(f.Vm.VisibleSharedTasks).Item.Title);
        f.Vm.SearchQuery = "  ГРУППОВОЙ   ДОКЛАД ";
        Assert.Empty(f.Vm.VisibleSharedTasks);
        f.Vm.StatusFilter = 1;
        Assert.Equal(done.HomeworkId, Assert.Single(f.Vm.VisibleSharedTasks).Item.HomeworkId);
        f.Vm.SearchQuery = "математика доклад";
        Assert.Equal(done.HomeworkId, Assert.Single(f.Vm.VisibleSharedTasks).Item.HomeworkId);
        f.Vm.ClearBrowseFiltersCommand.Execute(null);
        Assert.Equal(2, f.Vm.VisibleSharedTasks.Count);
    }

    [AvaloniaFact]
    public async Task Late_previous_group_list_cannot_repopulate_the_new_group()
    {
        using var f = new Fixture();
        await f.Load();
        var old = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        f.Copies = request => request.RequestUri!.AbsolutePath.Contains(CommunityId.ToString())
            ? MarkStarted() : Task.FromResult(Payload(new[] { Copy("Новое задание") }));
        Task<HttpResponseMessage> MarkStarted() { started = true; return old.Task; }
        var pending = f.Vm.RefreshSharedTasksCommand.ExecuteAsync(null);
        await Waits.Until(() => started, "old group request pending");
        f.SetGroup("3314");
        await f.Load();
        old.SetResult(Payload(new[] { Copy("Старое личное задание") }));
        await pending;
        Assert.Equal("Новое задание", Assert.Single(f.Vm.SharedTasks).Item.Title);
        Assert.False(f.Vm.SharedBusy);
    }

    [AvaloniaFact]
    public async Task Failed_refresh_after_completion_conflict_preserves_the_failure_message()
    {
        using var f = new Fixture();
        await f.Load();
        f.Complete = _ => Task.FromResult(Problem(409, "revision_conflict"));
        f.Copies = _ => Task.FromResult(Problem(503, "db_unavailable"));
        await f.Vm.SharedTasks[0].ToggleCommand.ExecuteAsync(null);
        Assert.Contains("не загрузились", f.Vm.SharedFeedback);
        Assert.DoesNotContain("Показана актуальная", f.Vm.SharedFeedback);
        Assert.False(f.Vm.SharedBusy);
        Assert.False(Assert.Single(f.Vm.SharedTasks).Item.Completed);
    }
}
