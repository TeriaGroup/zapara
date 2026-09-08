using System.Net;

namespace Zapara.Server.Tests;

internal sealed class InputHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    internal static HttpResponseMessage Response(HttpRequestMessage request, Stream body) => new(HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new StreamContent(body)
    };
}

internal sealed class InputStalledStream(Action entered) : Stream
{
    public bool Disposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        entered();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class InputClock : TimeProvider
{
    private TimerCallback? callback;
    private object? state;
    public TimeSpan DueTime { get; private set; }
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.callback = callback;
        this.state = state;
        DueTime = dueTime;
        return new ClockTimer();
    }
    public void Expire() => callback!(state);
    private sealed class ClockTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
