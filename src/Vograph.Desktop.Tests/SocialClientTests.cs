using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Vograph.Core.Services.Social;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Social;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class SocialClientTests
{
    private static readonly Guid Conversation = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Message = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Peer = Guid.Parse("cccccccc-cccc-4ccc-cccc-cccccccccccc");
    private static readonly Uri Root = new("https://example.invalid/");
    private static readonly string Access = Token("za_");

    [Fact]
    public async Task Routes_and_payloads_follow_native_social_contract()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new SocialHttpClient(http, Root);
        var paths = new List<string>();
        var bodies = new List<string>();
        handler.Send = async (request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Access, request.Headers.Authorization?.Parameter);
            paths.Add(request.RequestUri!.AbsolutePath + request.RequestUri.Query);
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            object result = paths.Count switch
            {
                1 or 2 => new SocialHomeResponse("ABCD1234", [], [], []),
                3 => new SocialPageResponse([], false),
                _ => new SocialMessageResponse(Message, Peer, "Друг", "text", "Привет", null, null, null,
                    null, Now, null, null, null, false, false, null, [])
            };
            var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(result, AccountJson.CreateOptions()));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(paths.Count == 4 ? HttpStatusCode.Created : HttpStatusCode.OK) { Content = content };
        };

        await client.HomeAsync(Access, TestContext.Current.CancellationToken);
        await client.InviteAsync(Access, "ABCD1234", TestContext.Current.CancellationToken);
        await client.MessagesAsync(Access, Conversation, Message, TestContext.Current.CancellationToken);
        await client.SendTextAsync(Access, Conversation, "Привет", Message, TestContext.Current.CancellationToken);

        Assert.Equal(["/api/v2/social/home", "/api/v2/social/invites",
            $"/api/v2/social/conversations/{Conversation:D}/messages?before={Message:D}",
            $"/api/v2/social/conversations/{Conversation:D}/messages"], paths);
        Assert.Contains("\"code\":\"ABCD1234\"", bodies[1]);
        Assert.Contains($"\"replyTo\":\"{Message:D}\"", bodies[3]);
    }

    [Fact]
    public async Task Decline_invitation_uses_the_existing_social_route()
    {
        var friendship = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new SocialHttpClient(http, Root);
        handler.Send = (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v2/social/invites/{friendship:D}/decline", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            return Task.FromResult(Json(new SocialHomeResponse("ABCD1234", [], [], [])));
        };

        var result = await client.DeclineAsync(Access, friendship, TestContext.Current.CancellationToken);
        Assert.Empty(result.Incoming);
    }

    [Fact]
    public async Task Multipart_photo_and_document_use_social_routes_and_attachment_is_bounded()
    {
        var attachment = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new SocialHttpClient(http, Root);
        var sent = new List<string>();
        handler.Send = async (request, _) =>
        {
            Assert.Equal(Access, request.Headers.Authorization?.Parameter);
            sent.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent([1, 2, 3]) };
            Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType?.MediaType);
            var form = await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("name=file", form);
            Assert.Contains("replyTo", form);
            var kind = sent.Count == 1 ? "image" : "file";
            var message = new SocialMessageResponse(Message, Peer, "Друг", kind, null, attachment,
                kind == "image" ? "Фото.webp" : "notes.pdf", "application/octet-stream", 3, Now,
                null, null, null, false, false, null, []);
            return Json(message, HttpStatusCode.Created);
        };

        await client.SendMediaAsync(Access, Conversation, "image", "photo.png", [1, 2, 3], Message, TestContext.Current.CancellationToken);
        await client.SendMediaAsync(Access, Conversation, "file", "notes.pdf", [1, 2, 3], Message, TestContext.Current.CancellationToken);
        var bytes = await client.ReadAttachmentAsync(Access, attachment, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], bytes);
        Assert.Equal([$"/api/v2/social/conversations/{Conversation:D}/images",
            $"/api/v2/social/conversations/{Conversation:D}/files",
            $"/api/v2/social/attachments/{attachment:D}"], sent);
        await Assert.ThrowsAsync<SocialClientException>(() => client.SendMediaAsync(Access, Conversation, "file", "large.pdf",
            new byte[20 * 1024 * 1024 + 1], ct: TestContext.Current.CancellationToken));
    }
}
