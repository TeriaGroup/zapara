using System.Net.Sockets;
using System.Text;
using Vograph.Desktop.Services;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileLanTests
{
    [Fact]
    public async Task Active_LAN_handler_and_accept_loop_are_cancelled_and_drained_before_account_activation()
    {
        await using var h = new ProfileHarness();
        h.Guest.LanSync.Dispose();
        h.Guest.LanSync = new LanSyncServer(h.Guest, 0, localhostOnly: true);
        h.Guest.LanSync.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync("127.0.0.1", h.Guest.LanSync.Port, TestContext.Current.CancellationToken);
        await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST /sync HTTP/1.1\r\nContent-Length: 100\r\n\r\n{"), TestContext.Current.CancellationToken);
        // The loop owns one lease; the accepted incomplete body owns another.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (h.Guest.Work.Outstanding < 2) await Task.Delay(5, deadline.Token);
        var result = await h.Coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken);
        Assert.True(result.Committed);
        Assert.True(h.Guest.IsClosed);
        Assert.Equal(0, h.Guest.Work.Outstanding);
        Assert.False(h.Guest.LanSync.IsRunning);
        Assert.False(h.Coordinator.Current.Services.LanSync.IsRunning);
    }
}
