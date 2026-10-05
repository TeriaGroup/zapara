using System.Net;
using System.Security.Cryptography;
using System.Text;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public class InputTransportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task File_stream_limit_and_cleanup(int extra)
    {
        var directory = Path.Combine(Path.GetTempPath(), "zapara-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "synthetic.xml");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(InputTests.Padded(InputTests.Limit + extra));
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            if (extra == 0)
            {
                var source = await InputTests.Input.FromFileAsync(path, TestContext.Current.CancellationToken);
                Assert.Equal(bytes, source.Bytes.ToArray());
                Assert.Null(source.SourceUrl);
                Assert.Null(source.SourceModifiedAt);
            }
            else Assert.Equal(FailureCode.SourceRejected, (await Assert.ThrowsAsync<TimetableInputException>(() => InputTests.Input.FromFileAsync(path, TestContext.Current.CancellationToken))).FailureCode);
        }
        finally { Directory.Delete(directory, true); }
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Missing_file_is_safe_and_external_cancellation_propagates()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "private.xml");
        var error = await Assert.ThrowsAsync<TimetableInputException>(() => InputTests.Input.FromFileAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(FailureCode.SourceRejected, error.FailureCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(path, error.ToString());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InputTests.Input.FromFileAsync(path, cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Http_counted_body_ignores_content_length(int extra)
    {
        var bytes = Encoding.UTF8.GetBytes(InputTests.Padded(InputTests.Limit + extra));
        using var client = new HttpClient(new InputHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(TimetableParser.DefaultUrl, request.RequestUri!.AbsoluteUri);
            var response = InputHandler.Response(request, new MemoryStream(bytes));
            response.Content.Headers.ContentLength = 1;
            response.Content.Headers.ContentEncoding.Add("identity");
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.FromHours(3));
            return Task.FromResult(response);
        }));
        if (extra != 0)
        {
            Assert.Equal(FailureCode.SourceRejected, (await Assert.ThrowsAsync<TimetableInputException>(() => InputTests.Input.FetchFixedAsync(client, TestContext.Current.CancellationToken))).FailureCode);
            return;
        }
        var source = await InputTests.Input.FetchFixedAsync(client, TestContext.Current.CancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        bytes[0] = 0;
        Assert.Equal(hash, source.SourceSha256);
        Assert.NotEqual(bytes[0], source.Bytes[0]);
        Assert.Equal(SourceKind.Http, source.SourceKind);
        Assert.Equal(TimetableParser.DefaultUrl, source.SourceUrl);
        Assert.Equal(TimeSpan.Zero, source.SourceModifiedAt!.Value.Offset);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("redirect")]
    [InlineData("uri")]
    [InlineData("error")]
    public async Task Http_rejections_do_not_expose_details(string kind)
    {
        using var client = new HttpClient(new InputHandler((request, _) =>
        {
            if (kind == "error") throw new HttpRequestException("private credentials");
            var response = InputHandler.Response(request, new MemoryStream());
            if (kind == "redirect") response.StatusCode = HttpStatusCode.Redirect;
            else if (kind == "uri") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/private");
            else response.Content.Headers.ContentEncoding.Add(kind);
            return Task.FromResult(response);
        }));
        var error = await Assert.ThrowsAsync<TimetableInputException>(() => InputTests.Input.FetchFixedAsync(client, TestContext.Current.CancellationToken));
        Assert.Equal(FailureCode.SourceRejected, error.FailureCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private", error.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Overall_deadline_covers_headers_and_body_and_external_cancel_wins(bool body, bool external)
    {
        var clock = new InputClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stream = new InputStalledStream(() => entered.TrySetResult());
        using var client = new HttpClient(new InputHandler(async (request, ct) =>
        {
            if (!body) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            return InputHandler.Response(request, stream);
        }));
        var task = new TimetableInput(clock).FetchFixedAsync(client, cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.DueTime);
        if (external) cancellation.Cancel(); else clock.Expire();
        if (external) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else Assert.Equal(FailureCode.SourceTimeout, (await Assert.ThrowsAsync<TimetableInputException>(() => task)).FailureCode);
        if (body) Assert.True(stream.Disposed);
        stream.Dispose();
    }

    [Fact]
    public void Factory_returns_owned_client_without_a_competing_timeout()
    {
        using var client = TimetableInput.CreateHttpClient();
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
