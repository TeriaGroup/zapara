using System.Net;
using Xunit;
using Zapara.Server.Admin;

namespace Zapara.Server.Tests;

public sealed class AdminAuthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Razor_admin_does_not_issue_a_session_or_render_the_operator_ui()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        var user = await host.RegisterAsync("admin.cookie");
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.Accounts.DataSource, db.Configuration, user.UserId, Ct));
        foreach (var path in new[] { "/Admin", "/Admin/Login", "/Admin/Accounts", "/Admin/Communities", "/Admin/Logout" })
        {
            using var response = await host.Client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("Создать сообщество", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Администрирование", body, StringComparison.Ordinal);
        }
        using var post = await host.Client.PostAsync("/Admin/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin.cookie", ["Password"] = AccountTestSupport.Password
        }), Ct);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.admin_sessions"));
    }

    [Fact]
    public async Task Anonymous_and_non_admin_requests_do_not_open_the_razor_ui()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        using var anonymous = await host.Client.GetAsync("/Admin", Ct);
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
        await host.RegisterAsync("not.admin");
        using var post = await host.Client.PostAsync("/Admin/Communities", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Группа"
        }), Ct);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.admin_sessions"));
        using var bootstrap = await host.Client.PostAsync("/api/v1/admin/bootstrap", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, bootstrap.StatusCode);
        using var live = await host.Client.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Disabled_module_routes_are_absent_and_http_never_migrates()
    {
        await using var db = await AdminPostgresFixture.CreateAsync();
        await using (var host = new AdminTestHost(db, adminEnabled: false))
        {
            using var login = await host.Client.GetAsync("/Admin/Login", Ct);
            Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
            using var admin = await host.Client.GetAsync("/Admin", Ct);
            Assert.Equal(HttpStatusCode.NotFound, admin.StatusCode);
        }
        await using (var host = new AdminTestHost(db))
        {
            using var login = await host.Client.GetAsync("/Admin/Login", Ct);
            Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
            Assert.Equal(0L, await db.TableCountAsync());
        }
    }
}
