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
        var result = await DataBootstrap.RunAsync(db.Services, allowNetwork: false);

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

        var result = await DataBootstrap.RunAsync(services, allowNetwork: false);

        Assert.False(result.HasData);
        Assert.True(result.Stale);
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
