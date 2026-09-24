using System.Net;
using System.Net.Sockets;
using System.Text;
using Vograph.Desktop.Services.Accounts;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ExternalLoopbackTests
{
    [Theory]
    [InlineData("wrong-id")]
    [InlineData("duplicate")]
    [InlineData("wrong-host")]
    [InlineData("wrong-path")]
    [InlineData("wrong-method")]
    [InlineData("escaped")]
    public async Task Invalid_requests_do_not_consume_pending_callback(string invalid)
    {
        using var callback = new ExternalLoopback();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var id = Guid.NewGuid();
        var code = new string('A', 43);
        var received = callback.ReceiveAsync(id, deadline.Token);
        var query = $"transactionId={id:D}&handoffCode={code}";
        var badQuery = invalid switch
        {
            "wrong-id" => $"transactionId={Guid.NewGuid():D}&handoffCode={code}",
            "duplicate" => query + $"&transactionId={id:D}",
            "escaped" => query.Replace("handoffCode=", "handoffCode=%41"),
            _ => query
        };
        var method = invalid == "wrong-method" ? "POST" : "GET";
        var path = invalid == "wrong-path" ? "/other" : "/zapara/oauth/callback";
        var host = invalid == "wrong-host" ? "attacker.invalid" : $"127.0.0.1:{callback.Port}";
        Assert.StartsWith("HTTP/1.1 400", await Send(callback.Port, $"{method} {path}?{badQuery} HTTP/1.1\r\nHost: {host}\r\n\r\n", deadline.Token));
        Assert.False(received.IsCompleted);
        Assert.StartsWith("HTTP/1.1 200", await Send(callback.Port, $"GET /zapara/oauth/callback?{query} HTTP/1.1\r\nHost: 127.0.0.1:{callback.Port}\r\n\r\n", deadline.Token));
        Assert.Equal(code, await received);
    }

    [Fact]
    public async Task Bound_port_cannot_be_claimed_and_cancellation_stops_wait()
    {
        using var callback = new ExternalLoopback();
        var other = new TcpListener(IPAddress.Loopback, callback.Port);
        try { Assert.Throws<SocketException>(() => other.Start()); }
        finally { other.Stop(); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waiting = callback.ReceiveAsync(Guid.NewGuid(), cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private static async Task<string> Send(int port, string request, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
