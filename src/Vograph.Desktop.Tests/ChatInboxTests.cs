using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Core.Services.Social;
using Vograph.Desktop.Features.Chat;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Communities;
using Zapara.Contracts.Social;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ChatInboxTests
{
    private static readonly Guid GroupChat = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly Guid GroupDirect = Guid.Parse("aaaaaaaa-2222-4222-8222-222222222222");
    private static readonly Guid SocialDirect = Guid.Parse("aaaaaaaa-3333-4333-8333-333333333333");
    private static readonly Guid First = Guid.Parse("bbbbbbbb-1111-4111-8111-111111111111");
    private static readonly Guid Second = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
    private static readonly Guid Third = Guid.Parse("bbbbbbbb-3333-4333-8333-333333333333");
    private static readonly Guid Fourth = Guid.Parse("bbbbbbbb-4444-4444-8444-444444444444");

    private static SocialHomeResponse FriendHome() => new("ABCD1234",
        [new SocialFriendResponse(PromotedId, "friend", "Друг", SocialDirect, "Привет", Now, 3)], [], []);

    private static SocialMessageResponse Message(Guid id, string body, IReadOnlyList<SocialReactionResponse>? reactions = null)
        => new(id, PromotedId, "Друг", "text", body, null, null, null, null, Now,
            null, null, null, false, false, null, reactions ?? []);

    [AvaloniaFact]
    public async Task Inbox_includes_group_chat_group_direct_and_social_friend()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var communities = new CommunityHttpClient(http, Root);
        using var social = new SocialHttpClient(http, Root);
        services.UseCommunities(communities, _ => Task.FromResult<string?>(Access));
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => AccountClientTestSupport.Json(new SocialHomeResponse("ABCD1234",
                [new SocialFriendResponse(PromotedId, "friend", "Друг", SocialDirect, "Привет", Now, 3)], [], [])),
            "/root/api/v2/communities" => Payload(new[] { Membership }),
            var path when path.EndsWith("/home", StringComparison.Ordinal) => Payload(new GroupHomeResponse(
                CommunityId, "О3313", "О3313",
                new ConversationResponse(GroupChat, "group", CommunityId, "О3313", null, "Группа", Now, 1),
                [], [new ConversationResponse(GroupDirect, "direct", CommunityId, "Борис", PromotedId, "Лично", Now.AddMinutes(1), 2)])),
            var path when path.Contains(GroupDirect.ToString("D"), StringComparison.Ordinal) && path.EndsWith("/messages", StringComparison.Ordinal)
                => Payload(new ChatPageResponse([new(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), GroupDirect,
                    PromotedId, "Борис", "Лично", Now, "text")], false)),
            var path when path.Contains(GroupDirect.ToString("D"), StringComparison.Ordinal) && path.EndsWith("/read", StringComparison.Ordinal)
                => Payload(new ConversationResponse(GroupDirect, "direct", CommunityId, "Борис", PromotedId, "Лично", Now, 0)),
            _ => Problem(404, "not_found")
        });

        var shell = new ShellViewModel(services);
        var inbox = new ChatInboxViewModel(services, shell);
        await inbox.ActivateAsync();

        Assert.True(inbox.Chats.Count == 3, $"Expected three chats, got {inbox.Chats.Count}: {inbox.Status}");
        var groupDirect = Assert.Single(inbox.Chats.Where(row => row.ConversationId == GroupDirect));
        Assert.Equal(CommunityId, groupDirect.CommunityId);
        Assert.Equal("Личный в группе", groupDirect.Kind);
        Assert.Equal("2", groupDirect.Unread);
        Assert.Contains(inbox.Chats, row => row.ConversationId == GroupChat && row.Kind == "Группа");
        Assert.Contains(inbox.Chats, row => row.ConversationId == SocialDirect && row.Personal);
        groupDirect.OpenCommand.Execute(null);
        await Waits.Until(() => shell.Current is Vograph.Desktop.Features.Groups.GroupViewModel group
            && group.IsDirect && group.Messages.Count == 1, "group direct opened from inbox");
        Assert.Equal(SectionKey.Group, shell.CurrentKey);
        Assert.Equal("Лично", ((Vograph.Desktop.Features.Groups.GroupViewModel)shell.Current!).Messages[0].Body);
    }

    [AvaloniaFact]
    public async Task Group_rows_survive_social_home_outage()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var communities = new CommunityHttpClient(http, Root);
        using var social = new SocialHttpClient(http, Root);
        services.UseCommunities(communities, _ => Task.FromResult<string?>(Access));
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable),
            "/root/api/v2/communities" => Payload(new[] { Membership }),
            var path when path.EndsWith("/home", StringComparison.Ordinal) => Payload(new GroupHomeResponse(
                CommunityId, "О3313", "О3313",
                new ConversationResponse(GroupChat, "group", CommunityId, "О3313", null, "Группа", Now, 1), [], [])),
            _ => Problem(404, "not_found")
        });

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services));
        await inbox.ActivateAsync();

        Assert.False(inbox.NeedAccount);
        Assert.Equal(GroupChat, Assert.Single(inbox.Chats).ConversationId);
        Assert.Contains("Личные", inbox.Status);
    }

    [AvaloniaFact]
    public async Task Older_personal_page_keeps_server_chronology()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var social = new SocialHttpClient(http, Root);
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => AccountClientTestSupport.Json(FriendHome()),
            var path when path.EndsWith("/messages", StringComparison.Ordinal)
                => AccountClientTestSupport.Json(request.RequestUri.Query.Length == 0
                    ? new SocialPageResponse([Message(Third, "Третье"), Message(Fourth, "Четвёртое")], true)
                    : new SocialPageResponse([Message(First, "Первое"), Message(Second, "Второе")], false)),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        });

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services));
        await inbox.ActivateAsync();
        Assert.Single(inbox.Chats).OpenCommand.Execute(null);
        await Waits.Until(() => inbox.Messages.Count == 2, "personal chat opened");
        await inbox.LoadOlderCommand.ExecuteAsync(null);

        Assert.Equal([First, Second, Third, Fourth], inbox.Messages.Select(item => item.Id).ToArray());
    }

    [AvaloniaFact]
    public async Task Personal_reaction_uses_server_code_like()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var social = new SocialHttpClient(http, Root);
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        string? sentCode = null;
        handler.Send = async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/root/api/v2/social/home") return AccountClientTestSupport.Json(FriendHome());
            if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
                return AccountClientTestSupport.Json(new SocialPageResponse([Message(First, "Привет")], false));
            if (request.Method == HttpMethod.Post && path.EndsWith("/reaction", StringComparison.Ordinal))
            {
                using var document = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
                sentCode = document.RootElement.GetProperty("emoji").GetString();
                return sentCode == "like"
                    ? AccountClientTestSupport.Json(Message(First, "Привет", [new("like", 1, true)]))
                    : new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        };

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services));
        await inbox.ActivateAsync();
        Assert.Single(inbox.Chats).OpenCommand.Execute(null);
        await Waits.Until(() => inbox.Messages.Count == 1, "personal chat opened");
        await inbox.Messages[0].ReactCommand.ExecuteAsync(null);

        Assert.Equal("like", sentCode);
        Assert.Contains("like 1", inbox.Messages[0].Reactions);
    }

    [AvaloniaFact]
    public async Task Active_chat_polls_and_keeps_older_loaded_messages()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var social = new SocialHttpClient(http, Root);
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        var latest = false;
        var homeReads = 0;
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => Home(),
            var path when path.EndsWith("/messages", StringComparison.Ordinal)
                => AccountClientTestSupport.Json(request.RequestUri.Query.Length != 0
                    ? new SocialPageResponse([Message(First, "Первое")], false)
                    : latest
                        ? new SocialPageResponse([Message(Second, "Второе изменено"), Message(Third, "Третье"), Message(Fourth, "Четвёртое")], true)
                        : new SocialPageResponse([Message(Second, "Второе"), Message(Third, "Третье")], true)),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        });
        HttpResponseMessage Home() { homeReads++; return AccountClientTestSupport.Json(FriendHome()); }

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services)) { PollInterval = TimeSpan.FromMilliseconds(100) };
        await inbox.ActivateAsync();
        Assert.Single(inbox.Chats).OpenCommand.Execute(null);
        await Waits.Until(() => inbox.Messages.Count == 2, "personal chat opened");
        await inbox.LoadOlderCommand.ExecuteAsync(null);
        Assert.Equal([First, Second, Third], inbox.Messages.Select(row => row.Id).ToArray());
        latest = true;
        inbox.Watch(true);
        await Waits.Until(() => inbox.Messages.Count == 4 && inbox.Messages[1].Display == "Второе изменено",
            "inbox and active chat polled");
        Assert.Equal([First, Second, Third, Fourth], inbox.Messages.Select(row => row.Id).ToArray());
        Assert.True(homeReads >= 2);
        inbox.Watch(false);
        var readsAfterStop = homeReads;
        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.Equal(readsAfterStop, homeReads);
    }

    [AvaloniaFact]
    public async Task Refresh_keeps_previous_rows_during_transient_outage()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var communities = new CommunityHttpClient(http, Root);
        using var social = new SocialHttpClient(http, Root);
        services.UseCommunities(communities, _ => Task.FromResult<string?>(Access));
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        var unavailable = false;
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => unavailable
                ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                : AccountClientTestSupport.Json(FriendHome()),
            "/root/api/v2/communities" => unavailable ? Problem(503, "db_unavailable") : Payload(new[] { Membership }),
            var path when path.EndsWith("/home", StringComparison.Ordinal) => Payload(new GroupHomeResponse(
                CommunityId, "О3313", "О3313",
                new ConversationResponse(GroupChat, "group", CommunityId, "О3313", null, "Группа", Now, 1), [], [])),
            _ => Problem(404, "not_found")
        });

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services));
        await inbox.ActivateAsync();
        Assert.Equal(2, inbox.Chats.Count);
        unavailable = true;
        await inbox.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, inbox.Chats.Count);
        Assert.Contains(inbox.Chats, row => row.ConversationId == GroupChat);
        Assert.Contains(inbox.Chats, row => row.ConversationId == SocialDirect);
    }

    [AvaloniaFact]
    public async Task Poll_does_not_start_another_request_while_one_is_in_flight()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var social = new SocialHttpClient(http, Root);
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        var reads = 0;
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Send = async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath != "/root/api/v2/social/home")
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            reads++;
            return reads == 1 ? AccountClientTestSupport.Json(FriendHome()) : await release.Task.WaitAsync(ct);
        };

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services)) { PollInterval = TimeSpan.FromMilliseconds(50) };
        await inbox.ActivateAsync();
        inbox.Watch(true);
        try
        {
            await Waits.Until(() => reads == 2, "second home request started");
            await Task.Delay(250, TestContext.Current.CancellationToken);
            Assert.Equal(2, reads);
        }
        finally
        {
            inbox.Watch(false);
            release.TrySetResult(AccountClientTestSupport.Json(FriendHome()));
        }
    }

    [AvaloniaFact]
    public async Task Poll_walks_older_pages_until_it_reaches_known_message()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var social = new SocialHttpClient(http, Root);
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        var newest = Guid.Parse("bbbbbbbb-5555-4555-8555-555555555555");
        var latest = false;
        var cursors = new List<string>();
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => AccountClientTestSupport.Json(FriendHome()),
            var path when path.EndsWith("/messages", StringComparison.Ordinal) => Page(request.RequestUri.Query),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        });
        HttpResponseMessage Page(string query)
        {
            cursors.Add(query);
            if (!latest) return AccountClientTestSupport.Json(new SocialPageResponse([Message(First, "Первое")], false));
            return AccountClientTestSupport.Json(query switch
            {
                "" => new SocialPageResponse([Message(Fourth, "Четвёртое"), Message(newest, "Пятое")], true),
                var value when value == $"?before={Fourth:D}" => new SocialPageResponse([Message(Second, "Второе"), Message(Third, "Третье")], true),
                var value when value == $"?before={Second:D}" => new SocialPageResponse([Message(First, "Первое")], false),
                _ => new SocialPageResponse([], false)
            });
        }

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services)) { PollInterval = TimeSpan.FromMilliseconds(50) };
        await inbox.ActivateAsync();
        Assert.Single(inbox.Chats).OpenCommand.Execute(null);
        await Waits.Until(() => inbox.Messages.Count == 1, "old personal message opened");
        latest = true;
        inbox.Watch(true);
        try
        {
            await Waits.Until(() => inbox.Messages.Count == 5, "all missed pages loaded");
            Assert.Equal([First, Second, Third, Fourth, newest], inbox.Messages.Select(row => row.Id).ToArray());
            Assert.Contains($"?before={Fourth:D}", cursors);
            Assert.Contains($"?before={Second:D}", cursors);
        }
        finally { inbox.Watch(false); }
    }

    [AvaloniaFact]
    public async Task Personal_photo_send_and_attachment_save_use_file_dialogs()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        var source = Path.Combine(directory.Root, "photo.png");
        var destination = Path.Combine(directory.Root, "saved.webp");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        var dialogs = new FakeFileDialogs { OpenPath = source, SavePath = destination };
        services.FileDialogs = dialogs;
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var social = new SocialHttpClient(http, Root);
        services.UseSocial(social, _ => Task.FromResult<string?>(Access));
        var attachmentId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var sent = false;
        var downloaded = false;
        var photo = new SocialMessageResponse(First, PromotedId, "Друг", "image", null, attachmentId,
            "photo.webp", "image/webp", 4, Now, null, null, null, false, false, null, []);
        handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/root/api/v2/social/home" => AccountClientTestSupport.Json(FriendHome()),
            var path when path.EndsWith("/messages", StringComparison.Ordinal)
                => AccountClientTestSupport.Json(new SocialPageResponse([], false)),
            var path when request.Method == HttpMethod.Post && path.EndsWith("/images", StringComparison.Ordinal)
                => Uploaded(),
            var path when request.Method == HttpMethod.Get && path.EndsWith($"/attachments/{attachmentId:D}", StringComparison.Ordinal)
                => Downloaded(),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        });
        HttpResponseMessage Uploaded() { sent = true; return AccountClientTestSupport.Json(photo, System.Net.HttpStatusCode.Created); }
        HttpResponseMessage Downloaded() { downloaded = true; return new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([5, 6, 7]) }; }

        var inbox = new ChatInboxViewModel(services, new ShellViewModel(services));
        await inbox.ActivateAsync();
        Assert.Single(inbox.Chats).OpenCommand.Execute(null);
        await Waits.Until(() => inbox.HasConversation, "personal chat opened");
        await inbox.AttachCommand.ExecuteAsync("image");
        Assert.True(sent);
        var row = Assert.Single(inbox.Messages);
        Assert.Equal("Фото", row.Display);
        Assert.True(row.CanDownload);
        await row.DownloadCommand!.ExecuteAsync(null);
        Assert.True(downloaded);
        Assert.Equal("photo.webp", dialogs.LastSuggestedName);
        Assert.Equal([5, 6, 7], File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }
}
