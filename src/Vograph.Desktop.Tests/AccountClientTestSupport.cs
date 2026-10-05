using System.Net;
using System.Text;
using System.Text.Json;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Tests;

internal static class AccountClientTestSupport
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    internal static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid FamilyId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    internal static readonly Guid DeviceId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    internal const string Password = "synthetic password only";
    internal static string Token(string prefix, byte value = 1) => prefix + Convert.ToBase64String(
        Enumerable.Repeat(value, 32).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static UserResponse User => new(UserId, "Test.User", null, Now.AddDays(-1));
    internal static SessionResponse Session(byte value = 1, Guid? family = null, Guid? user = null) => new(
        new(user ?? UserId, "Test.User", null, Now.AddDays(-1)), family ?? FamilyId,
        Token("za_", value), Token("zr_", value), "Bearer", Now.AddMinutes(15), Now.AddDays(30));
    internal static LoginRequest Login => new("Test.User", Password, new(DeviceId, "Windows", "windows"));
    internal static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        => Raw(JsonSerializer.Serialize(body, AccountJson.CreateOptions()), status);
    internal static HttpResponseMessage Raw(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class AccountClientHandler : HttpMessageHandler
{
    internal new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send { get; set; }
        = (_, _) => Task.FromResult(AccountClientTestSupport.Json(AccountClientTestSupport.Session()));
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Send(request, ct);
    }
}

internal sealed class AccountClientClock : TimeProvider
{
    internal DateTimeOffset Now = AccountClientTestSupport.Now;
    private TimerCallback? callback;
    private object? state;
    public override DateTimeOffset GetUtcNow() => Now;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.callback = callback;
        this.state = state;
        return new TimerStub();
    }
    internal void Expire() => callback!(state);
    private sealed class TimerStub : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
