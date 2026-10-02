using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Vograph.Core.Services.Communities;
using Xunit;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class GroupViewModelMediaTests
{
    [AvaloniaFact]
    public async Task Send_posts_photo_video_and_file_from_the_open_chat()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        services.UseCommunities(client, _ => Task.FromResult<string?>(Access));
        var uploads = new List<(string Kind, byte[] Body)>();
        var requests = new List<string>();
        var held = new List<string>();
        var conversationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var chat = new ConversationResponse(conversationId, "group", CommunityId, "О3313", null, null, null, 0);
        var home = new GroupHomeResponse(CommunityId, "О3313", "О3313", chat,
            [new ClassmateResponse(UserId, "student", "Аня", "member", true)], []);
        var general = new GroupTopicResponse(null, "Чат группы", "user", null, null, null, 0,
            true, "chat", permissions: ["read", "post", "media"]);
        var desk = new GroupDeskResponse(false, [], [], [], [], []);
        handler.Send = async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(request.Method + " " + path);
            if (request.Method == HttpMethod.Get && path.EndsWith("/communities", StringComparison.Ordinal))
                return Payload(new[] { Membership });
            if (request.Method == HttpMethod.Get && path.EndsWith("/home", StringComparison.Ordinal))
                return Payload(home);
            if (request.Method == HttpMethod.Get && path.EndsWith("/space", StringComparison.Ordinal))
                return Payload(new GroupSpaceResponse([general], [], new(Powers: ["read", "post"]), desk));
            if (request.Method == HttpMethod.Get && path.EndsWith("/desk", StringComparison.Ordinal))
                return Payload(desk);
            if (request.Method == HttpMethod.Get && path.EndsWith("/topics", StringComparison.Ordinal))
                return Payload(new GroupTopicListResponse([general]));
            if (request.Method == HttpMethod.Get && path.EndsWith("/messages", StringComparison.Ordinal))
                return Payload(new ChatPageResponse([], false));
            if (request.Method == HttpMethod.Post && path.EndsWith("/read", StringComparison.Ordinal))
                return Payload(chat);
            if (request.Method == HttpMethod.Get && path.EndsWith("/media", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    { Content = new ByteArrayContent([1, 2, 3, 4]) };
                response.Content.Headers.ContentType = new("application/octet-stream");
                return response;
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/media", StringComparison.Ordinal))
            {
                var kind = request.Headers.GetValues("X-Zapara-Kind").Single();
                var body = await request.Content!.ReadAsByteArrayAsync(ct);
                uploads.Add((kind, body));
                var label = kind switch { "image" => "Фото", "video" => "Видео", _ => "notes.txt" };
                return Payload(new ChatMessageResponse(Guid.NewGuid(), conversationId, UserId, "Аня", label, AccountClientTestSupport.Now, kind), System.Net.HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && (path.Contains("/delete", StringComparison.Ordinal) || path.Contains("/react", StringComparison.Ordinal)))
            {
                held.Add(path.Contains("/delete", StringComparison.Ordinal) ? "delete" : "reaction");
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var marker = Array.IndexOf(parts, "messages");
                var messageId = Guid.Parse(parts[marker + 1]);
                var removed = path.Contains("/delete", StringComparison.Ordinal);
                return Payload(new ChatMessageResponse(messageId, conversationId, UserId, "Аня", "Фото", AccountClientTestSupport.Now, "image", removed));
            }
            return Problem(404, "not_found");
        };
        var vm = new GroupViewModel(services);
        await vm.ActivateAsync();
        Assert.True(vm.HasHome);
        Assert.True(vm.ShowComposer, $"chat={vm.ShowMessages}, selected={vm.SelectedChannel?.Title}, status={vm.Status}");
        Assert.True(vm.CanAttachMedia, $"busy={vm.IsBusy}, selected={vm.SelectedChannel?.Title}, status={vm.Status}");
        var dialogs = new FakeFileDialogs();
        services.FileDialogs = dialogs;
        var photo = new byte[] { 1, 2, 3, 4 };
        var clip = new byte[] { 9, 8, 7 };
        var notes = "конспект"u8.ToArray();
        vm.Draft = "Черновик сообщения";
        dialogs.OpenPath = Write(directory.Root, "снимок.png", photo);
        await vm.AttachCommand.ExecuteAsync("image");
        Assert.True(uploads.Count == 1, $"status={vm.Status}; requests={string.Join(" | ", requests)}");
        Assert.True(vm.Messages.Count == 1, $"status={vm.Status}; selected={vm.SelectedChannel?.Title}; requests={string.Join(" | ", requests)}");
        dialogs.OpenPath = Write(directory.Root, "ролик.mp4", clip);
        await vm.AttachCommand.ExecuteAsync("video");
        Assert.True(vm.Messages.Count == 2, $"after video status={vm.Status}; selected={vm.SelectedChannel?.Title}; requests={string.Join(" | ", requests)}");
        dialogs.OpenPath = Write(directory.Root, "notes.txt", notes);
        await vm.AttachCommand.ExecuteAsync("file");
        Assert.True(vm.Messages.Count == 3, $"after file status={vm.Status}; selected={vm.SelectedChannel?.Title}; requests={string.Join(" | ", requests)}");
        Assert.Equal(["image", "video", "file"], vm.Messages.Select(item => item.Kind).ToArray());
        Assert.Equal(["Фото", "Видео", "notes.txt"], vm.Messages.Select(item => item.Display).ToArray());
        Assert.Equal(["image", "video", "file"], uploads.Select(item => item.Kind).ToArray());
        Assert.Equal(photo, uploads[0].Body);
        Assert.Equal(clip, uploads[1].Body);
        Assert.Equal(notes, uploads[2].Body);
        Assert.Equal("Черновик сообщения", vm.Draft);
        var photoRow = vm.Messages[0];
        photoRow.Apply("edit");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", vm.HoldCaption);
        photoRow.Apply("reply");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Ответ", vm.HoldCaption);
        photoRow.Apply("reaction:like");
        for (var i = 0; i < 30 && !held.Contains("reaction"); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        var reacted = vm.Messages.Single(item => item.Kind == "image");
        reacted.Apply("delete");
        Assert.True(vm.HasPendingDeleteMessage);
        await vm.ConfirmDeleteMessageCommand.ExecuteAsync(null);
        for (var i = 0; i < 30 && !vm.Messages.Any(item => item.Kind == "image" && item.Deleted); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.Contains("reaction", held);
        Assert.Contains("delete", held);
        Assert.Contains(vm.Messages, item => item.Kind == "image" && item.Deleted);
        vm.Detach();
    }

    private static string Write(string root, string name, byte[] bytes)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
