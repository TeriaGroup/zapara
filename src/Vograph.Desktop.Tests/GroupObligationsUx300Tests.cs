using Avalonia.Headless.XUnit;
using System.Net;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public class GroupObligationsUx300Tests
{
    [Fact]
    public void One_timeline_merges_three_authorized_kinds_and_marks_actual_needed_actions()
    {
        var formsTopic = Guid.NewGuid(); var ballotTopic = Guid.NewGuid();
        GroupTopicResponse[] topics =
        [new(formsTopic, "Анкеты", "?", null, null, null, 0, false, "forms", permissions: ["read", "formsRespond"]),
         new(ballotTopic, "Голосования", "?", null, null, null, 0, false, "ballots", permissions: ["read", "vote"])];
        var homework = new GroupHomeworkCopyResponse(Guid.NewGuid(), "Задача", "Текст", 1, false, 0, Now.AddDays(1));
        var form = new GroupFormResponse(Guid.NewGuid(), formsTopic, "Анкета", "", Now.AddDays(2), false,
            [], StaffId, Now, true, false, null, 0);
        var ballot = new BallotResponse(Guid.NewGuid(), "Выбор", "collective", "open", Now.AddDays(3),
            0, 1, false, [], "", "", ballotTopic);

        var rows = GroupObligationPlanner.Merge([homework], [form], [ballot], topics);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.True(row.NeedsMe));
        Assert.Equal(["homework", "form", "ballot"], rows.Select(row => row.Kind));
        Assert.True(rows[0].NeedsAction(Now.AddDays(4)));
        Assert.False(rows[1].NeedsAction(Now.AddDays(4)));
        Assert.False(rows[2].NeedsAction(Now.AddDays(4)));
    }

    [Fact]
    public void Authorized_general_ballot_needs_vote_without_a_synthetic_vote_topic()
    {
        GroupTopicResponse[] topics =
        [new(null, "Общий чат", "?", null, null, null, 0, true, "chat",
            permissions: ["read", "post"])];
        var ballot = new BallotResponse(Guid.NewGuid(), "Выбрать дату", "collective", "open",
            Now.AddDays(1), 0, 1, false, [], "", "");
        var result = Assert.Single(GroupObligationPlanner.Merge([], [], [ballot], topics));
        Assert.True(result.NeedsAction(Now));
    }

    [AvaloniaFact]
    public async Task Form_obligation_rechecks_exact_id_before_opening_its_topic()
    {
        using var directory = new ProfileTestDirectory();
        using var guest = AppServices.Create(directory.Root, () => false);
        var profile = ProfileDescriptor.Account(directory.Root, new string('A', 64), StaffId);
        Directory.CreateDirectory(Path.GetDirectoryName(profile.DatabasePath)!);
        using var app = guest.CreateProfile(profile);
        app.AllowNetwork = false;
        await app.Parser.RefreshAsync(xmlOverride: File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "sample-timetable.xml")));
        var settings = app.Db.GetSettings(); settings.MyGroupId = TestDb.MyGroupId; app.Db.SaveSettings(settings);
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        app.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var topic = Guid.NewGuid(); var conversation = Guid.NewGuid();
        var topics = new GroupTopicListResponse([new(topic, "Анкеты", "?", null, null, null, 0,
            false, "forms", permissions: ["read", "forms", "formsRespond"])]);
        var desk = new GroupDeskResponse(false, [], [], [], [], []);
        var home = new GroupHomeResponse(CommunityId, "Группа", TestDb.MyGroupId,
            new(conversation, "group", CommunityId, "Чат", null, null, null, 0),
            [new(StaffId, "self_user", "Я", "member", true)], []);
        var id = Guid.NewGuid();
        var form = new GroupFormResponse(id, topic, "Ответить сегодня", "", Now.AddDays(1),
            false, [], StaffId, Now, true, false, null, 0);
        var paths = new List<string>();
        handler.Send = (request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath + request.RequestUri.Query);
            return Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
            var path when path.EndsWith("/communities") => Payload(new[] { Membership }),
            var path when path.EndsWith("/home") => Payload(home),
            var path when path.EndsWith("/desk") => Payload(desk),
            var path when path.EndsWith("/space") => Payload(new GroupSpaceResponse(topics.Topics, [], new(), desk)),
            var path when path.EndsWith("/topics") => Payload(topics),
            var path when path.EndsWith("/forms") => Payload(new GroupFormListResponse([form])),
            var path when path.EndsWith("/homework/copies") => Payload(Array.Empty<GroupHomeworkCopyResponse>()),
            var path when path.EndsWith("/ballots") => Payload(new BallotBoardResponse(false, false, false, 3, 2, [])),
            var path when path.EndsWith("/messages") => Payload(new ChatPageResponse([], false)),
            var path when path.EndsWith("/read") => Payload(home.GroupChat),
            _ => Problem(404, "not_found")
            });
        };
        var vm = new GroupViewModel(app);
        vm.RequestCommunity(CommunityId);
        await vm.ActivateAsync();
        Assert.True(vm.HasHome, vm.Status + " · " + string.Join(" | ", paths));
        await vm.LoadGroupObligationsCommand.ExecuteAsync(null);
        Assert.True(vm.ObligationRows.Count > 0, vm.ObligationsStatus);
        var row = Assert.Single(vm.ObligationRows, choice => choice.Entry.Id == id);
        Assert.True(row.Entry.NeedsMe);
        (string Kind, Guid Id)? focused = null;
        vm.ObligationFocusRequested += (kind, target) => focused = (kind, target);

        await row.OpenCommand.ExecuteAsync(null);

        Assert.Equal(("form", id), focused);
        Assert.Contains(vm.Forms, existing => existing.Form.FormId == id);
        vm.Detach();
    }
}
