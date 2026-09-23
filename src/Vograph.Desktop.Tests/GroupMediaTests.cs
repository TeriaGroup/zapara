using System.Net;
using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Xunit;
using Zapara.Client.Domain;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Tests;

public sealed class GroupMediaTests
{
    [AvaloniaFact]
    public async Task Place_sends_the_photo_and_the_bubble_can_be_held()
    {
        var photo = new byte[] { 1, 2, 3, 4 };
        var seen = new Captured();
        var handler = new AccountClientHandler { Send = async (request, ct) =>
        {
            seen.Kind = request.Headers.GetValues("X-Zapara-Kind").Single();
            seen.Body = await request.Content!.ReadAsByteArrayAsync(ct);
            seen.Url = request.RequestUri!.AbsolutePath;
            var conversation = Guid.Parse("22222222-2222-4222-8222-222222222222");
            var message = new ChatMessageResponse(Guid.Parse("11111111-1111-4111-8111-111111111111"), conversation,
                Guid.Parse("33333333-3333-4333-8333-333333333333"), "Аня", seen.Kind == "image" ? "Фото" : "notes.txt",
                new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), seen.Kind);
            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new ByteArrayContent(CommunityJson.Serialize(message))
            };
            response.Content.Headers.ContentType = new("application/json");
            return response;
        } };
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, new Uri("http://127.0.0.1:9/"));
        var saved = await GroupMedia.Place(client, AccountClientTestSupport.Token("za_"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "image", "папка\\снимок.png", photo, null, CancellationToken.None);
        Assert.Equal("image", saved.Kind);
        Assert.Equal("Фото", saved.Body);
        Assert.Equal("image", seen.Kind);
        Assert.Equal(photo, seen.Body);
        Assert.EndsWith("/conversations/22222222-2222-4222-8222-222222222222/media", seen.Url);
        var called = new List<string>();
        var row = new GroupMessageRow(saved.MessageId, saved.SenderName, saved.Body, "сейчас", true, saved.Kind, saved.Deleted, called.Add);
        Assert.Equal("Фото", row.Display);
        var box = new HoldBox { DataContext = row };
        box.Choose("reply");
        box.Choose("reaction");
        box.Choose("edit");
        box.Choose("delete");
        Assert.Equal(["reply", "reaction", "delete"], called);
        var clipBytes = new byte[] { 9, 8, 7 };
        var clip = await GroupMedia.Place(client, AccountClientTestSupport.Token("za_"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "video", "ролик.mp4", clipBytes, null, CancellationToken.None);
        Assert.Equal("video", clip.Kind);
        Assert.Equal(clipBytes, seen.Body);
        Assert.Equal("video", seen.Kind);
        var clipCalled = new List<string>();
        var clipRow = new GroupMessageRow(clip.MessageId, clip.SenderName, clip.Body, "сейчас", true, clip.Kind, clip.Deleted, clipCalled.Add);
        Assert.Equal("Видео", clipRow.Display);
        var clipBox = new HoldBox { DataContext = clipRow };
        clipBox.Choose("reply");
        clipBox.Choose("edit");
        clipBox.Choose("delete");
        Assert.Equal(["reply", "delete"], clipCalled);
        var notesBytes = "конспект"u8.ToArray();
        var notes = await GroupMedia.Place(client, AccountClientTestSupport.Token("za_"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "file", "notes.txt", notesBytes, null, CancellationToken.None);
        Assert.Equal("file", notes.Kind);
        Assert.Equal(notesBytes, seen.Body);
    }

    private sealed class Captured
    {
        public string Kind { get; set; } = "";
        public byte[] Body { get; set; } = [];
        public string Url { get; set; } = "";
    }
}
