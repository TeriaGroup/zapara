using System.Net;
using System.Text.Json;
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

public class HomeworkBulkPublishUx300Tests
{
    [AvaloniaTheory]
    [InlineData("transport")]
    [InlineData("malformed-success")]
    [InlineData("oversize-success")]
    public async Task Uncertain_batch_retries_frozen_payload_and_operation_id_even_after_local_edit(string firstResult)
    {
        var physicalRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("VOGRAPH_TEST_DATA_ROOT") ??
            Path.Combine(Path.GetTempPath(), "vograph-tests"));
        var root = physicalRoot.StartsWith(@"\\?\", StringComparison.Ordinal) ? physicalRoot : @"\\?\" + physicalRoot;
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var guest = AppServices.Create(directory, () => false);
        var profile = ProfileDescriptor.Account(directory, new string('A', 64), StaffId);
        Directory.CreateDirectory(Path.GetDirectoryName(profile.DatabasePath)!);
        using var app = guest.CreateProfile(profile);
        app.AllowNetwork = false;
        await app.Parser.RefreshAsync(xmlOverride: File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "sample-timetable.xml")));
        var settings = app.Db.GetSettings(); settings.MyGroupId = TestDb.MyGroupId; app.Db.SaveSettings(settings);
        var id = app.Homework.AddHomework(TestDb.MathSubject, "Решить задачи", 1,
            createdAt: new DateTime(2026, 9, 5));
        var staleId = app.Homework.AddHomework(TestDb.MathSubject, "Второе задание", 1,
            createdAt: new DateTime(2026, 9, 5));
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        app.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var groupChat = new ConversationResponse(Guid.NewGuid(), "group", CommunityId, "Чат", null, null, null, 0);
        var home = new GroupHomeResponse(CommunityId, "Группа", TestDb.MyGroupId, groupChat,
            [new(StaffId, "self_user", "Я", "member", true)], []);
        var desk = new GroupDeskResponse(false, [], [], [], [], [], new(HomeworkAudience: true));
        var requests = new List<HomeworkUpsert>();
        var created = new Dictionary<Guid, HomeworkResponse>();
        handler.Send = async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/homework/share"))
            {
                var value = JsonSerializer.Deserialize<HomeworkUpsert>(await request.Content!.ReadAsStringAsync(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                requests.Add(value);
                var operationId = Assert.IsType<Guid>(value.OperationId);
                if (!created.TryGetValue(operationId, out var saved))
                {
                    saved = new HomeworkResponse(Guid.NewGuid(), CommunityId, value.Title, value.Body, 1,
                        Now, Now, value.DeadlineAt, value.TopicId, value.Audience);
                    created.Add(operationId, saved);
                    if (firstResult == "transport") throw new HttpRequestException("response lost");
                    if (firstResult == "malformed-success") return new HttpResponseMessage(HttpStatusCode.Created)
                    { Content = new StringContent("{broken", System.Text.Encoding.UTF8, "application/json") };
                    return new HttpResponseMessage(HttpStatusCode.Created)
                    { Content = new StringContent(new string('x', CommunityValidation.RequestBytes + 1),
                        System.Text.Encoding.UTF8, "application/json") };
                }
                return Payload(saved, HttpStatusCode.Created);
            }
            if (path.EndsWith("/home")) return Payload(home);
            if (path.EndsWith("/desk")) return Payload(desk);
            if (path.EndsWith("/homework/copies")) return Payload(Array.Empty<GroupHomeworkCopyResponse>());
            return Payload(new[] { Membership });
        };
        var vm = new HomeworkViewModel(app, new ShellViewModel(app), () => new DateTime(2026, 9, 6));
        try
        {
            await vm.LoadAsync();
            vm.BulkMode = true;
            var selected = vm.Groups.SelectMany(group => group.Items).Single(row => row.Entry.Homework.Id == id);
            Assert.NotEmpty(selected.Entry.SubjectRaw);
            Assert.NotEqual("done", app.Homework.GetById(id)!.Status);
            _ = new HomeworkUpsert(selected.Entry.SubjectRaw.Trim(), app.Homework.GetById(id)!.Text.Trim(), 0,
                app.Homework.GetById(id)!.DueDateComputed is { } due
                    ? new DateTimeOffset(due.Year, due.Month, due.Day, 23, 59, 59, TimeSpan.FromHours(3)).ToUniversalTime() : null,
                null, HomeworkAudience.All, Guid.NewGuid());
            selected.SelectedForBulk = true;
            vm.Groups.SelectMany(group => group.Items).Single(row => row.Entry.Homework.Id == staleId).SelectedForBulk = true;
            await vm.LoadBulkPublishRecipientsCommand.ExecuteAsync(null);
            Assert.True(vm.ShowBulkPublishRecipients);
            await vm.PreviewBulkPublicationCommand.ExecuteAsync(null);
            Assert.True(vm.ShowBulkPublishPreview, vm.BulkPublishFeedback + " · selected=" + vm.BulkSelectedCount);
            app.Homework.UpdateHomework(staleId, "Внешняя правка до отправки", 1);
            await vm.ConfirmBulkPublicationCommand.ExecuteAsync(null);
            Assert.True(vm.HasPendingBulkPublication);
            Assert.Contains("изменились до отправки: 1", vm.BulkPublishFeedback);
            app.Homework.UpdateHomework(id, "Правка только локальной копии", 1);
            await vm.ConfirmBulkPublicationCommand.ExecuteAsync(null);
            Assert.False(vm.HasPendingBulkPublication);
            Assert.Equal(2, requests.Count);
            Assert.Single(created);
            Assert.Equal(requests[0].OperationId, requests[1].OperationId);
            Assert.Equal("Решить задачи", requests[1].Body);
            Assert.Equal("Правка только локальной копии", app.Homework.GetById(id)!.Text);
        }
        finally
        {
            vm.Detach();
            app.Dispose(); guest.Dispose();
            var full = Path.GetFullPath(directory);
            if (full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in Directory.EnumerateFiles(full, "*.db", SearchOption.AllDirectories))
                { using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
                    Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection); }
                Directory.Delete(full, true);
            }
        }
    }
}
