using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    [Fact]
    public async Task Adoption_catalog_then_selection_loads_actual_typed_cache()
    {
        using var handler = new FakeHttpHandler { Respond = r => Json(r.RequestUri!.AbsolutePath.EndsWith("/groups") ? Catalog() : Schedule()) };
        using var http = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, "https://example.invalid/", uri => new(http, uri));
            Assert.True(app.Api.Configured);
            Assert.True(await app.Api.RefreshAsync(ct: TestContext.Current.CancellationToken));
            Assert.Null(app.Db.GetSettings().LastFetchedAt);
            Assert.Null(new TimetableApiCache(app.Db).Read("a"));
            var settings = app.Db.GetSettings();
            settings.MyGroupId = "a";
            app.Db.SaveSettings(settings);
            Assert.True(await app.Api.RefreshAsync(neededOnly: true, ct: TestContext.Current.CancellationToken));
            Assert.Equal(Guid.Parse(Pin), new TimetableApiCache(app.Db).Read("a")!.Meta!.SnapshotId);
            Assert.Equal("2026-09-01", app.Db.GetSettings().PeriodStart);
        }
        finally { CleanupApiDirectory(dir); }
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("http://example.invalid/")]
    [InlineData("   ")]
    public void Adoption_bad_configuration_is_explicit_not_legacy(string url)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var app = AppServices.Create(dir, () => false, url);
            Assert.True(app.Api.Configured);
            Assert.NotNull(app.Api.ConfigurationError);
            Assert.DoesNotContain(url, app.Api.ConfigurationError);
        }
        finally { CleanupApiDirectory(dir); }
    }

    private static void CleanupApiDirectory(string dir)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dir, "vograph.db")}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        Directory.Delete(dir, true);
    }
}
