using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class GroupChannelLoadRaceTests
{
    private static readonly Guid OtherCommunity = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid OtherChat = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid OtherTopic = Guid.Parse("77777777-7777-4777-8777-777777777777");

    [AvaloniaTheory]
    [InlineData("topics")]
    [InlineData("desk")]
    public async Task Late_group_loader_cannot_replace_the_group_selected_after_it(string heldRoute)
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Send = async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/communities", StringComparison.Ordinal))
                return Payload(new[] { Membership, new CommunityResponse(OtherCommunity, "О3314", "Группа Б", 1, "member") });
            var other = path.Contains(OtherCommunity.ToString("D"), StringComparison.Ordinal);
            if (path.EndsWith("/home", StringComparison.Ordinal))
                return Payload(Home(other));
            if (path.EndsWith("/topics", StringComparison.Ordinal))
            {
                if (!other && heldRoute == "topics") { started.TrySetResult(); return await release.Task.WaitAsync(ct); }
                return Payload(Topics(other));
            }
            if (path.EndsWith("/desk", StringComparison.Ordinal))
            {
                if (!other && heldRoute == "desk") { started.TrySetResult(); return await release.Task.WaitAsync(ct); }
                return Payload(new GroupDeskResponse(false, [], [], [], [], []));
            }
            if (path.EndsWith("/messages", StringComparison.Ordinal)) return Payload(new ChatPageResponse([], false));
            return Problem(404, "not_found");
        };

        var vm = new GroupViewModel(services);
        vm.RequestCommunity(CommunityId);
        var first = vm.ActivateAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            vm.RequestCommunity(OtherCommunity);
            await vm.ActivateAsync();
            release.TrySetResult(heldRoute == "topics" ? Payload(Topics(false))
                : Payload(new GroupDeskResponse(true, [new(PollId, "Доверенный")], [], [], [], ["channels"])));
            await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal("Группа Б", vm.HomeTitle);
            Assert.Contains(vm.Channels, channel => channel.TopicId == OtherTopic);
            Assert.DoesNotContain(vm.Channels, channel => channel.TopicId == PollId);
            Assert.False(vm.IsHeadman);
        }
        finally
        {
            release.TrySetResult(Problem(503, "db_unavailable"));
            vm.Detach();
        }
    }

    private static GroupHomeResponse Home(bool other)
    {
        var community = other ? OtherCommunity : CommunityId;
        var chat = other ? OtherChat : HomeworkId;
        var name = other ? "Группа Б" : "Группа А";
        return new(community, name, name,
            new ConversationResponse(chat, "group", community, name, null, null, null, 0),
            [new ClassmateResponse(UserId, "student", "Аня", "member", true)], []);
    }

    private static GroupTopicListResponse Topics(bool other) => new([
        new GroupTopicResponse(null, "Общий", "💬", null, null, null, 0, false),
        new GroupTopicResponse(other ? OtherTopic : PollId, other ? "Раздел Б" : "Раздел А", "📚", null, null, null, 0, false)
    ], false);
}
