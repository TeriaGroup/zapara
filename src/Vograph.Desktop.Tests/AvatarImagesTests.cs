using System.Net;
using System.Net.Http.Headers;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Vograph.Core.Services.Social;
using Vograph.Desktop.Features.Chat;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class AvatarImagesTests
{
    [AvaloniaFact]
    public async Task Webp_decodes_and_invalidation_refetches()
    {
        var id = Guid.NewGuid();
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
        var payload = encoded.ToArray();
        var count = 0;
        using var handler = new FakeHttpHandler
        {
            Respond = _ =>
            {
                count++;
                var response = FakeHttpHandler.Bytes(payload);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
                return response;
            }
        };
        using var http = new HttpClient(handler);
        using var store = new AvatarImages(new AvatarHttpClient(http, new Uri("https://example.invalid/")));
        var token = Token("za_");
        Assert.NotNull(await store.UserAsync(token, id, TestContext.Current.CancellationToken));
        var visible = await store.UserAsync(token, id, TestContext.Current.CancellationToken);
        Assert.NotNull(visible);
        Assert.Equal(128, visible.PixelSize.Width);
        Assert.Equal(1, count);
        store.InvalidateUser(id);
        Assert.Equal(128, visible.PixelSize.Width); // cache invalidation cannot dispose a displayed image
        Assert.NotNull(await store.UserAsync(token, id, TestContext.Current.CancellationToken));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Failed_auth_clears_earlier_photo()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var requests = 0;
        using var handler = new FakeHttpHandler
        {
            Respond = _ =>
            {
                requests++;
                if (requests == 2) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        using var http = new HttpClient(handler);
        using var store = new AvatarImages(new AvatarHttpClient(http, new Uri("https://example.invalid/")));
        var token = Token("za_");
        Assert.Null(await store.UserAsync(token, first, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<AvatarClientException>(() => store.UserAsync(token, second, TestContext.Current.CancellationToken));
        Assert.Null(await store.UserAsync(token, first, TestContext.Current.CancellationToken));
        Assert.Equal(3, requests);
    }

    [AvaloniaFact]
    public async Task Late_response_cannot_restore_invalidated_photo()
    {
        var id = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var handler = new DelayedHandler(async ct =>
        {
            count++;
            if (count == 1) { started.SetResult(); await release.Task.WaitAsync(ct); }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        using var store = new AvatarImages(new AvatarHttpClient(http, new Uri("https://example.invalid/")));
        var token = Token("za_");
        var first = store.UserAsync(token, id, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        store.InvalidateUser(id);
        release.SetResult();
        Assert.Null(await first);
        Assert.Null(await store.UserAsync(token, id, TestContext.Current.CancellationToken));
        Assert.Equal(2, count);
    }

    private sealed class DelayedHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(cancellationToken);
    }
}
