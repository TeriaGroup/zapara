using System.Net;
using System.Text;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleRefresherTests
{
    private static readonly string Xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));

    [Fact]
    public async Task Not_Modified_Answer_Skips_The_Download()
    {
        var handler = new FakeHttpHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified) };
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync("2026-09-05T10:00:00.0000000Z", TestContext.Current.CancellationToken);

        Assert.False(check.Modified);
        var head = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Head, head.Method);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero), head.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Modified_Answer_Downloads_And_Decodes_Utf16()
    {
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Xml)).ToArray();
        var handler = new FakeHttpHandler
        {
            Respond = r => r.Method == HttpMethod.Head
                ? FakeHttpHandler.Bytes(Array.Empty<byte>(), lastModified: DateTimeOffset.UtcNow)
                : FakeHttpHandler.Bytes(utf16)
        };
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync("2026-09-01T10:00:00.0000000Z", TestContext.Current.CancellationToken);

        Assert.True(check.Modified);
        Assert.Contains("<Group Number=\"А863С\"", check.Xml);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task No_Timestamp_Means_Full_Download_Without_Head()
    {
        var handler = new FakeHttpHandler { Respond = _ => FakeHttpHandler.Bytes(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Xml)).ToArray()) };
        using var refresher = new ScheduleRefresher(handler);

        var check = await refresher.CheckAsync(null, TestContext.Current.CancellationToken);

        Assert.True(check.Modified);
        Assert.StartsWith("<?xml", check.Xml);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task Network_Failure_Propagates_To_The_Caller()
    {
        var handler = new FakeHttpHandler { Respond = _ => throw new HttpRequestException("offline") };
        using var refresher = new ScheduleRefresher(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => refresher.CheckAsync(null, TestContext.Current.CancellationToken));
    }
}
