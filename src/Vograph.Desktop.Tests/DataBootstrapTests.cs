using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class DataBootstrapTests
{
    [Theory]
    [InlineData(0, null, true)]
    [InlineData(5, null, true)]
    [InlineData(5, "garbage", true)]
    [InlineData(5, "2026-09-04T10:00:00.0000000Z", false)]
    [InlineData(5, "2026-09-01T10:00:00.0000000Z", true)]
    public void NeedsRefresh_Rules(int groups, string? lastFetched, bool expected)
    {
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, DataBootstrap.NeedsRefresh(groups, lastFetched, now));
    }

    [Fact]
    public async Task Seeded_Db_Is_Fresh_And_Needs_No_Refresh()
    {
        using var db = TestDb.Create();
        var result = await DataBootstrap.RunAsync(db.Services); // no XML: what an offline caller hands in

        Assert.True(result.HasData);
        Assert.False(result.Refreshed);
        Assert.False(result.Stale);
        Assert.Equal(3, db.Services.Db.GetAllGroups().Count);
    }

    [Fact]
    public async Task Empty_Db_Without_Network_Reports_No_Data()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.Create(dir);

        var result = await DataBootstrap.RunAsync(services, fetchError: "offline");

        Assert.False(result.HasData);
        Assert.True(result.Stale);
        Assert.Equal("offline", result.Error); // the reason the caller's fetch gave, carried through to the error state
    }

    [Fact]
    public async Task Fetch_Html_json_falls_back_to_xml()
    {
        using var db = TestDb.Create();
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
        db.Services.Refresher = new ScheduleRefresher(new FakeHttpHandler
        {
            Respond = r =>
            {
                var uri = r.RequestUri ?? throw new InvalidOperationException("missing uri");
                if (uri.AbsoluteUri.Contains("TimetableGroup50.xml", StringComparison.Ordinal))
                    return FakeHttpHandler.Text(xml);
                return FakeHttpHandler.Text(VoenmehHttp.CachedHtml);
            }
        });

        var fetch = await DataBootstrap.FetchAsync(db.Services);

        Assert.Null(fetch.Parsed);
        Assert.Null(fetch.Error);
        Assert.Contains("<Timetable>", fetch.Xml, StringComparison.Ordinal);
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);
    }

    [Fact]
    public async Task Empty_db_ingests_xml_fallback()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.Create(dir);
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));

        var result = await DataBootstrap.RunAsync(services, timetableXml: xml);

        Assert.True(result.HasData);
        Assert.True(result.Refreshed);
        Assert.False(result.Stale);
        Assert.Contains(services.Db.GetAllGroups(), g => g.Name == "А863С");
    }

    /// <summary>A fetch that failed outside the gate never throws into the caller — it comes back as the reason,
    /// which is what the bootstrap then reports.</summary>
    [Fact]
    public async Task Fetch_Reports_A_Dead_Network_As_A_Reason()
    {
        using var db = TestDb.Create();
        db.Services.Refresher = new ScheduleRefresher(new FakeHttpHandler { Respond = _ => throw new HttpRequestException("offline") });

        var fetch = await DataBootstrap.FetchAsync(db.Services);

        Assert.Null(fetch.Parsed);
        Assert.Equal("offline", fetch.Error);
        Assert.Equal(1, db.Services.CoreGate.CurrentCount); // the fetch never touches the gate
    }

    [Fact]
    public void TestDb_Seeds_Personalization()
    {
        using var db = TestDb.Create();
        Assert.Equal("Матан", db.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1));
        var hw = Assert.Single(db.Services.Homework.GetForSubject(TestDb.MathSubject));
        Assert.Equal(new DateTime(2026, 9, 7), hw.DueDateComputed!.Value.Date); // first ВЫСШ. МАТЕМАТ after 05.09 is Mon 07.09
        Assert.Single(db.Services.Db.GetFriends());
    }
}
