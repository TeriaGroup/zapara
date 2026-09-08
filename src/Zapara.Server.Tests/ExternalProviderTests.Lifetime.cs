using System.Net;
using Xunit;
using static Zapara.Server.Accounts.ExternalProviders.ExternalProviderTestsSupport;

namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class ExternalProviderTestsLifetime
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Finished_requests_release_token_headers_and_form_content(bool vk)
    {
        using var handler = new ExternalProviderTestsHandler();
        if (vk) handler.UserJson = "{\"user\":{\"user_id\":\"subject\"}}";
        using var http = new HttpClient(handler);
        var clock = new ExternalProviderTestsClock();
        using var adapter = Adapter(vk, http, clock);
        await Exchange(adapter, vk);
        Assert.All(handler.OriginalRequests, request => { Assert.Null(request.Content); Assert.Null(request.Headers.Authorization); });
        Assert.Equal(0, clock.LiveTimers);
        adapter.Dispose();
        // The injected client belongs to the caller, not the adapter.
        using var response = await http.GetAsync("https://example.invalid/test", TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task One_budget_covers_token_and_userinfo_and_caller_cancel_is_distinct(bool vk, bool cancelCaller)
    {
        var clock = new ExternalProviderTestsClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ExternalProviderTestsHandler();
        handler.Respond = async (number, ct) =>
        {
            if (number == 1) { clock.Advance(TimeSpan.FromSeconds(20)); return handler.Response(handler.TokenJson); }
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var adapter = Adapter(vk, http, clock);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = ExchangeWithCancellation(adapter, vk, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(task.IsCompleted);
        if (cancelCaller) cancel.Cancel(); else clock.Advance(TimeSpan.FromSeconds(1));
        if (cancelCaller)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(cancel.Token, error.CancellationToken);
        }
        else Assert.Equal(ExternalProviderFailure.Timeout, (await Assert.ThrowsAsync<ExternalProviderException>(() => task)).Failure);
        Assert.Equal(0, clock.LiveTimers);
        Assert.All(handler.Contents, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task Timeout_during_body_read_disposes_response_stream_and_timer()
    {
        var clock = new ExternalProviderTestsClock();
        using var stream = new BlockingStream();
        using var content = new StreamContent(stream);
        using var handler = new ExternalProviderTestsHandler
        {
            Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var adapter = Adapter(false, http, clock);
        var task = Exchange(adapter, false);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(ExternalProviderFailure.Timeout, (await Assert.ThrowsAsync<ExternalProviderException>(() => task)).Failure);
        Assert.True(stream.Disposed);
        Assert.Equal(0, clock.LiveTimers);
    }

    private sealed class BlockingStream : Stream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
