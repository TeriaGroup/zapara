using System.Net;
using System.Net.Http.Headers;
using Vograph.Core.Services.Social;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class AvatarClientTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    private static readonly string Access = Token("za_");

    [Fact]
    public async Task User_photo_is_bounded_and_authenticated()
    {
        using var handler = new FakeHttpHandler();
        using var http = new HttpClient(handler);
        using var client = new AvatarHttpClient(http, new Uri("https://example.invalid/"));
        handler.Respond = request =>
        {
            Assert.Equal($"/api/v2/social/avatars/users/{Id:D}", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Access, request.Headers.Authorization?.Parameter);
            var response = FakeHttpHandler.Bytes([0x52, 0x49, 0x46, 0x46]);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
            return response;
        };
        Assert.Equal([0x52, 0x49, 0x46, 0x46], await client.UserAsync(Access, Id, TestContext.Current.CancellationToken));
        handler.Respond = _ =>
        {
            var response = FakeHttpHandler.Bytes(new byte[512 * 1024 + 1]);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
            return response;
        };
        await Assert.ThrowsAsync<AvatarClientException>(() => client.UserAsync(Access, Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_photo_has_no_image_and_mutations_use_expected_routes()
    {
        using var handler = new FakeHttpHandler();
        using var http = new HttpClient(handler);
        using var client = new AvatarHttpClient(http, new Uri("https://example.invalid/"));
        var paths = new List<string>();
        handler.Respond = request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal("file", ((MultipartFormDataContent)request.Content).First().Headers.ContentDisposition?.Name?.Trim('"'));
                var response = FakeHttpHandler.Text("{\"revision\":\"aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa\"}");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return response;
            }
            return new HttpResponseMessage(request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.NotFound);
        };
        Assert.Null(await client.UserAsync(Access, Id, TestContext.Current.CancellationToken));
        await client.PutMeAsync(Access, "photo.png", [1, 2, 3], TestContext.Current.CancellationToken);
        await client.DeleteGroupAsync(Access, Id, TestContext.Current.CancellationToken);
        Assert.Equal([$"/api/v2/social/avatars/users/{Id:D}", "/api/v2/social/avatars/me",
            $"/api/v2/social/avatars/groups/{Id:D}"], paths);
        await Assert.ThrowsAsync<AvatarClientException>(() => client.PutMeAsync(Access, "huge.png", new byte[3 * 1024 * 1024 + 1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Body_read_obeys_the_same_deadline_as_response_headers()
    {
        using var handler = new FakeHttpHandler
        {
            Respond = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new StalledStream()) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
                return response;
            }
        };
        using var http = new HttpClient(handler);
        using var client = new AvatarHttpClient(http, new Uri("https://example.invalid/"), TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<AvatarClientException>(() => client.UserAsync(Access, Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, error.Status);
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
