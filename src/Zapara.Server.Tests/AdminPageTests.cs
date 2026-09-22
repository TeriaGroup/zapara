using System.Net;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class AdminPageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Razor_operator_pages_are_not_served()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        foreach (var path in new[]
        {
            "/Admin", "/Admin/Communities", "/Admin/Staff", "/Admin/Memberships",
            "/Admin/Accounts", "/Admin/Content", "/Admin/Audit"
        })
        {
            using var response = await host.Client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("Назначить персонал", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Отключить аккаунт", body, StringComparison.Ordinal);
            Assert.DoesNotContain("<form", body, StringComparison.OrdinalIgnoreCase);
        }
        using (var fonts = await host.Client.GetAsync("/fonts/inter_regular.ttf", Ct))
            Assert.Equal(HttpStatusCode.OK, fonts.StatusCode);
        using (var css = await host.Client.GetAsync("/css/admin.css", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, css.StatusCode);
            var text = await css.Content.ReadAsStringAsync(Ct);
            Assert.Contains("inter_regular", text, StringComparison.Ordinal);
        }
    }
}
