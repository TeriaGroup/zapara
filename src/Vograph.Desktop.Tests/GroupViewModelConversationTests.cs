using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class GroupViewModelConversationTests
{
    private static readonly Guid GroupChatId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DirectChatId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid DirectMessageId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid FourthId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid FifthId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid SixthId = Guid.Parse("66666666-6666-4666-8666-666666666666");

    [AvaloniaFact]
    public async Task Editing_an_older_message_keeps_chronological_order()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(Respond(request, edit: true));

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        Assert.Equal([FirstId, SecondId], vm.Messages.Select(item => item.Id).ToArray());
        vm.Draft = "Обычный черновик";
        vm.Messages[0].Apply("edit");
        Dispatcher.UIThread.RunJobs();
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal([FirstId, SecondId], vm.Messages.Select(item => item.Id).ToArray());
        Assert.Equal("Изменено", vm.Messages[0].Body);
        Assert.Equal("Обычный черновик", vm.Draft);
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Drafts_stay_with_their_conversation_when_switching_between_group_and_direct()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(Respond(request, direct: true));

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        vm.Draft = "Черновик группы";
        vm.Messages[0].Apply("edit");
        Assert.Equal("Первое", vm.Draft);
        vm.Directs.Single().OpenCommand!.Execute(null);
        await Waits.Until(() => vm.IsDirect && vm.Messages.Any(item => item.Id == DirectMessageId), "direct chat opened");
        Assert.Equal("", vm.Draft);

        vm.Draft = "Личный черновик";
        await vm.BackToGroupCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirect);
        Assert.Equal("Черновик группы", vm.Draft);
        Assert.Equal("", vm.HoldCaption);
        vm.Directs.Single().OpenCommand!.Execute(null);
        await Waits.Until(() => vm.IsDirect && vm.Messages.Any(item => item.Id == DirectMessageId), "direct chat reopened");
        Assert.Equal("Личный черновик", vm.Draft);
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Requested_group_direct_opens_exact_conversation()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(Respond(request, direct: true));

        var vm = new GroupViewModel(services);
        vm.RequestConversation(CommunityId, DirectChatId);
        await vm.ActivateAsync();

        Assert.True(vm.IsDirect);
        Assert.Equal("Борис", vm.ChatTitle);
        Assert.Equal([DirectMessageId], vm.Messages.Select(item => item.Id).ToArray());
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Late_group_page_cannot_replace_the_direct_chat_that_opened_after_it()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Send = async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal)
                && request.RequestUri.AbsolutePath.Contains(GroupChatId.ToString("D"), StringComparison.Ordinal))
            {
                requested.TrySetResult();
                return await release.Task.WaitAsync(ct);
            }
            return Respond(request, direct: true);
        };

        var vm = new GroupViewModel(services);
        var opening = vm.ActivateAsync();
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            vm.Directs.Single().OpenCommand!.Execute(null);
            await Waits.Until(() => vm.IsDirect && vm.Messages.Any(item => item.Id == DirectMessageId), "direct chat loaded");
            release.TrySetResult(Payload(new ChatPageResponse([
                new(FirstId, GroupChatId, UserId, "Аня", "Первое", CommunityClientTestSupport.Now, "text")], false)));
            await opening.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(vm.IsDirect);
            Assert.Equal([DirectMessageId], vm.Messages.Select(item => item.Id).ToArray());
        }
        finally
        {
            release.TrySetResult(Payload(new ChatPageResponse([], false)));
            vm.Detach();
        }
    }

    [AvaloniaFact]
    public async Task Poll_updates_an_edited_message_and_adds_new_messages_without_losing_history()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var changed = false;
        var reads = 0;
        var topicReads = 0;
        handler.Send = (request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/read", StringComparison.Ordinal))
                reads++;
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal)
                && request.RequestUri.Query.Contains("topic=general", StringComparison.Ordinal)) topicReads++;
            if (changed && request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
                return Task.FromResult(Payload(new ChatPageResponse([
                    new(FirstId, GroupChatId, UserId, "Аня", "Новое содержание", CommunityClientTestSupport.Now, "text"),
                    new(SecondId, GroupChatId, UserId, "Аня", "Второе", CommunityClientTestSupport.Now.AddMinutes(1), "text"),
                    new(DirectMessageId, GroupChatId, PromotedId, "Борис", "Третье", CommunityClientTestSupport.Now.AddMinutes(2), "text")], false)));
            return Task.FromResult(Respond(request));
        };

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        vm.Watch(true);
        changed = true;
        await Waits.Until(() => vm.Messages.Count == 3 && vm.Messages[0].Body == "Новое содержание", "edited and new messages", 6500);
        Assert.Equal([FirstId, SecondId, DirectMessageId], vm.Messages.Select(item => item.Id).ToArray());
        await Waits.Until(() => topicReads >= 2, "general topic read on polling");
        Assert.Equal(0, reads);
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Chosen_reaction_and_later_count_are_visible_on_group_message()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var count = 0;
        string? sentEmoji = null;
        handler.Send = async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
                return Payload(new ChatPageResponse([new ChatMessageResponse(FirstId, GroupChatId, PromotedId,
                    "Борис", "Первое", CommunityClientTestSupport.Now, reactions:
                    [new("like", 2, false), new("heart", count, true)])], false));
            if (request.Method == HttpMethod.Post && path.EndsWith("/react", StringComparison.Ordinal))
            {
                sentEmoji = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(
                    TestContext.Current.CancellationToken)).RootElement.GetProperty("emoji").GetString();
                count = 1;
                return Payload(new ChatMessageResponse(FirstId, GroupChatId, PromotedId, "Борис", "Первое",
                    CommunityClientTestSupport.Now, reactions: [new("like", 2, false), new("heart", 1, true)]));
            }
            return Respond(request);
        };

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        Assert.Equal("👍 2", Assert.Single(vm.Messages[0].Reactions).Display);
        vm.Messages[0].Apply("reaction:heart");
        await Waits.Until(() => vm.Messages[0].Reactions.Any(item => item.Display == "❤️ 1 ✓"), "chosen reaction displayed");
        Assert.Equal("heart", sentEmoji);

        count = 2;
        vm.Watch(true);
        await Waits.Until(() => vm.Messages[0].Reactions.Any(item => item.Display == "❤️ 2 ✓"), "reaction count refreshed", 6500);
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Poll_fills_a_gap_larger_than_the_latest_page_before_showing_newest_messages()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var changed = false;
        var cursors = new List<string>();
        ChatMessageResponse Item(Guid id, int minute) => new(id, GroupChatId, PromotedId, "Борис", id.ToString("N"), CommunityClientTestSupport.Now.AddMinutes(minute), "text");
        handler.Send = (request, _) =>
        {
            if (changed && request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
            {
                cursors.Add(request.RequestUri.Query);
                return Task.FromResult(request.RequestUri.Query switch
                {
                    "?topic=general" => Payload(new ChatPageResponse([Item(FifthId, 5), Item(SixthId, 6)], true)),
                    var query when query == $"?topic=general&after={SecondId:D}" => Payload(new ChatPageResponse([Item(DirectMessageId, 3), Item(FourthId, 4)], true)),
                    var query when query == $"?topic=general&after={FourthId:D}" => Payload(new ChatPageResponse([Item(FifthId, 5), Item(SixthId, 6)], false)),
                    _ => Problem(400, "invalid_request")
                });
            }
            return Task.FromResult(Respond(request));
        };

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        vm.Watch(true);
        changed = true;
        await Waits.Until(() => vm.Messages.Count == 6, "missing group messages caught up", 6500);
        Assert.Equal([FirstId, SecondId, DirectMessageId, FourthId, FifthId, SixthId], vm.Messages.Select(item => item.Id).ToArray());
        Assert.Equal(["?topic=general", $"?topic=general&after={SecondId:D}", $"?topic=general&after={FourthId:D}"], cursors.Take(3).ToArray());
        vm.Detach();
    }

    [AvaloniaFact]
    public async Task Late_send_to_group_does_not_clear_or_enter_the_direct_chat_opened_while_it_waited()
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
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/topic-messages", StringComparison.Ordinal))
            {
                started.TrySetResult();
                return await release.Task.WaitAsync(ct);
            }
            return Respond(request, direct: true);
        };

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        vm.Draft = "Для группы";
        var sending = vm.SendCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            vm.Directs.Single().OpenCommand!.Execute(null);
            await Waits.Until(() => vm.IsDirect && vm.Messages.Any(item => item.Id == DirectMessageId), "direct chat while group send pending");
            vm.Draft = "Для личного чата";
            release.TrySetResult(Payload(new ChatMessageResponse(FourthId, GroupChatId, UserId, "Аня", "Для группы",
                CommunityClientTestSupport.Now.AddMinutes(4), "text"), System.Net.HttpStatusCode.Created));
            await sending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(vm.IsDirect);
            Assert.Equal("Для личного чата", vm.Draft);
            Assert.Equal([DirectMessageId], vm.Messages.Select(item => item.Id).ToArray());
            await vm.BackToGroupCommand.ExecuteAsync(null);
            Assert.Equal("", vm.Draft);
        }
        finally
        {
            release.TrySetResult(Problem(503, "db_unavailable"));
            vm.Detach();
        }
    }

    [AvaloniaFact]
    public async Task Media_message_offers_a_save_action_and_downloads_only_after_a_path_is_chosen()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var bytes = new byte[] { 1, 3, 5, 7 };
        var downloads = 0;
        handler.Send = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith($"/messages/{FirstId:D}/media", StringComparison.Ordinal))
            {
                downloads++;
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new("application/octet-stream");
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
                return Task.FromResult(Payload(new ChatPageResponse([
                    new(FirstId, GroupChatId, PromotedId, "Борис", "../photo.png", CommunityClientTestSupport.Now, "file")], false)));
            return Task.FromResult(Respond(request));
        };

        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        var media = Assert.Single(vm.Messages);
        Assert.True(media.CanDownload);
        var dialogs = new FakeFileDialogs();
        services.FileDialogs = dialogs;
        await media.DownloadCommand!.ExecuteAsync(null);
        Assert.Equal(0, downloads);

        var saved = Path.Combine(directory.Root, "photo.png");
        dialogs.SavePath = saved;
        await media.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("photo.png", dialogs.LastSuggestedName);
        Assert.Equal(bytes, File.ReadAllBytes(saved));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
        Assert.Equal(1, downloads);
        vm.Detach();
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, bool edit = false, bool direct = false)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/communities", StringComparison.Ordinal))
            return Payload(new[] { Membership });
        if (request.Method == HttpMethod.Get && path.EndsWith("/home", StringComparison.Ordinal))
            return Payload(Home(direct));
        if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
            return path.Contains(DirectChatId.ToString("D"), StringComparison.Ordinal)
                ? Payload(new ChatPageResponse([new(DirectMessageId, DirectChatId, UserId, "Аня", "Лично", CommunityClientTestSupport.Now, "text")], false))
                : Payload(new ChatPageResponse([
                    new(FirstId, GroupChatId, UserId, "Аня", "Первое", CommunityClientTestSupport.Now, "text"),
                    new(SecondId, GroupChatId, UserId, "Аня", "Второе", CommunityClientTestSupport.Now.AddMinutes(1), "text")], false));
        if (request.Method == HttpMethod.Post && path.EndsWith("/read", StringComparison.Ordinal))
            return Payload(new ConversationResponse(GroupChatId, "group", CommunityId, "О3313", null, null, null, 0));
        if (edit && request.Method == HttpMethod.Post && path.EndsWith("/edit", StringComparison.Ordinal))
            return Payload(new ChatMessageResponse(FirstId, GroupChatId, UserId, "Аня", "Изменено", CommunityClientTestSupport.Now, "text"));
        return Problem(404, "not_found");
    }

    private static GroupHomeResponse Home(bool direct)
    {
        var group = new ConversationResponse(GroupChatId, "group", CommunityId, "О3313", null, null, null, 0);
        var chats = direct ? new[] { new ConversationResponse(DirectChatId, "direct", CommunityId, "Борис", PromotedId, null, null, 0) } : [];
        return new GroupHomeResponse(CommunityId, "О3313", "О3313", group,
            [new ClassmateResponse(UserId, "student", "Аня", "member", true)], chats);
    }
}
