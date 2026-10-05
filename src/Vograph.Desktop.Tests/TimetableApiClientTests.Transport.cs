using System.Net;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Runs_four_group_requests_concurrently_but_never_more()
    {
        int active = 0, maximum = 0;
        var fourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ids = Enumerable.Range(0, 12).Select(i => "g" + i).ToArray();
        using var handler = new TransportHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/groups")) return Json(Catalog(Pin, ids));
            var current = Interlocked.Increment(ref active);
            lock (fourStarted) maximum = Math.Max(maximum, current);
            if (current == 4) fourStarted.TrySetResult();
            try
            {
                await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                await Task.Delay(10, ct);
                return Json(Schedule(request.RequestUri.Segments[^2].TrimEnd('/')));
            }
            finally { Interlocked.Decrement(ref active); }
        });
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(12, (await client.FetchAsync(ids, TestContext.Current.CancellationToken)).DownloadedGroups.Count);
        Assert.Equal(4, maximum);
        Assert.Equal(0, active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejects_oversize_body_with_or_without_content_length(bool lengthHeader)
    {
        using var stream = new GeneratedStream(16 * 1024 * 1024 + 1);
        using var handler = new FakeHttpHandler { Respond = _ =>
        {
            var content = new StreamContent(stream);
            if (lengthHeader) content.Headers.ContentLength = 16 * 1024 * 1024 + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }};
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        Assert.Equal(TimetableApiFailure.BodyTooLarge,
            (await Assert.ThrowsAsync<TimetableApiException>(() => client.FetchAsync([], TestContext.Current.CancellationToken))).Failure);
        Assert.True(stream.WasDisposed);
        Assert.True(stream.BytesRead <= 16 * 1024 * 1024 + 1);
    }

    [Fact]
    public async Task Cancellation_during_body_read_disposes_stream_and_preserves_caller_cancellation()
    {
        using var stream = new GeneratedStream(1024, block: true);
        using var handler = new FakeHttpHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(stream) } };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("https://example.invalid/"));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fetch = client.FetchAsync([], cts.Token);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task Escapes_opaque_id_as_single_path_segment_and_keeps_prefix()
    {
        const string id = "а/б? #";
        using var handler = new FakeHttpHandler { Respond = r => Json(
            r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog(Pin, id) : Schedule(id)) };
        using var http = new HttpClient(handler);
        using var client = new TimetableApiClient(http, new Uri("http://localhost:1234/configured/api/"));
        await client.FetchAsync([id], TestContext.Current.CancellationToken);
        Assert.Equal($"http://localhost:1234/configured/api/api/v1/groups/{Uri.EscapeDataString(id)}/timetable?snapshotId={Pin}",
            handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Injected_http_client_is_not_disposed_by_adapter()
    {
        using var handler = new FakeHttpHandler { Respond = _ => Json(Catalog()) };
        using var http = new HttpClient(handler);
        new TimetableApiClient(http, new Uri("https://example.invalid/")).Dispose();
        using var result = await http.GetAsync("https://example.invalid/test", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccessStatusCode);
    }

    private sealed class GeneratedStream(int length, bool block = false) : Stream
    {
        public bool WasDisposed { get; private set; }
        public int BytesRead { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Started.TrySetResult();
            if (block) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            var count = Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..count].Fill((byte)' ');
            BytesRead += count;
            return count;
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
