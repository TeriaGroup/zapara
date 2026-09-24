using System.Net;
using System.Net.Http.Headers;
using Vograph.Core.Services.Communities;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class CommunityMediaDownloadTests
{
    private static readonly Guid ConversationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid MessageId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Member_can_download_bounded_media_with_a_per_request_bearer()
    {
        var bytes = new byte[] { 0, 1, 2, 255 };
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://example.invalid/root/api/v2/communities/conversations/{ConversationId:D}/messages/{MessageId:D}/media",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer " + Access, request.Headers.Authorization?.ToString());
            Assert.Null(http.DefaultRequestHeaders.Authorization);
            return Task.FromResult(Binary(bytes));
        };

        Assert.Equal(bytes, await client.ReadMediaAsync(Access, ConversationId, MessageId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Oversized_media_is_rejected_before_it_reaches_the_view_model()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (_, _) => Task.FromResult(Binary(new byte[8 * 1024 * 1024 + 1]));

        var error = await Assert.ThrowsAsync<CommunityClientException>(() =>
            client.ReadMediaAsync(Access, ConversationId, MessageId, TestContext.Current.CancellationToken));
        Assert.Equal(CommunityClientFailure.BodyTooLarge, error.Failure);
    }

    private static HttpResponseMessage Binary(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
