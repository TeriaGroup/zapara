using System.Net;
using Xunit;
using Zapara.Server.Admin;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class AdminAuthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_cookie_is_secure_httponly_samesite_lax_and_csrf_is_required()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        var user = await host.RegisterAsync("admin.cookie");
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.Accounts.DataSource, db.Configuration, user.UserId, Ct));
        var html = await host.GetHtml("/Admin/Login");
        Assert.Contains("Вход", html, StringComparison.Ordinal);
        Assert.Contains("Имя пользователя", html, StringComparison.Ordinal);
        Assert.Contains("Пароль", html, StringComparison.Ordinal);
        Assert.Contains("Войти", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Login<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Password<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.googleapis", html, StringComparison.Ordinal);
        using (var missing = await host.PostForm("/Admin/Login", new Dictionary<string, string>
        {
            ["Username"] = "admin.cookie", ["Password"] = Password
        }, token: null))
            Assert.Equal(400, (int)missing.StatusCode);
        var setCookie = await host.LoginAsync("admin.cookie", Password);
        Assert.Contains(".Zapara.Admin=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("za_", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("zr_", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, setCookie, StringComparison.Ordinal);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.admin_sessions WHERE revoked_at IS NULL"));
        var home = await host.GetHtml("/Admin");
        Assert.Contains("Администрирование", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer ", home, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", home, StringComparison.Ordinal);
        using var logoutPage = await host.Client.GetAsync("/Admin/Logout", Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, logoutPage.StatusCode);
        using var logout = await host.PostForm("/Admin/Logout", new Dictionary<string, string>(), AdminTestHost.Antiforgery(home));
        Assert.Equal(302, (int)logout.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.admin_sessions WHERE revoked_at IS NULL"));
        using var after = await host.Client.GetAsync("/Admin", Ct);
        Assert.Equal(HttpStatusCode.Found, after.StatusCode);
        Assert.Contains("/Admin/Login", after.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_navigation_and_non_admin_login_do_not_issue_session()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        using (var anonymous = await host.Client.GetAsync("/Admin", Ct))
        {
            Assert.Equal(HttpStatusCode.Found, anonymous.StatusCode);
            Assert.Contains("/Admin/Login", anonymous.Headers.Location?.ToString(), StringComparison.Ordinal);
        }
        using (var post = await host.PostForm("/Admin/Communities", new Dictionary<string, string> { ["Name"] = "Группа" }, token: "not-a-token"))
            Assert.True((int)post.StatusCode is 400 or 302);
        var stranger = await host.RegisterAsync("not.admin");
        var failed = await host.LoginAsync("not.admin", Password, 200);
        Assert.DoesNotContain(".Zapara.Admin=", failed, StringComparison.Ordinal);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.admin_sessions"));
        var loginHtml = await host.GetHtml("/Admin/Login");
        Assert.Contains("Неверные данные для входа", await PostLoginExpectBody(host, "not.admin", Password), StringComparison.Ordinal);
        Assert.DoesNotContain(stranger.UserId.ToString("D"), loginHtml, StringComparison.Ordinal);
        using var bootstrap = await host.Client.PostAsync("/api/v1/admin/bootstrap", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, bootstrap.StatusCode);
        using var pageBootstrap = await host.Client.PostAsync("/Admin/Bootstrap", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, pageBootstrap.StatusCode);
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
            var html = await host.GetHtml("/Admin/Login");
            Assert.Contains("Вход", html, StringComparison.Ordinal);
            Assert.Equal(0L, await db.TableCountAsync());
        }
    }

    private static async Task<string> PostLoginExpectBody(AdminTestHost host, string username, string password)
    {
        var html = await host.GetHtml("/Admin/Login");
        using var response = await host.PostForm("/Admin/Login", new Dictionary<string, string>
        {
            ["Username"] = username, ["Password"] = password
        }, AdminTestHost.Antiforgery(html));
        Assert.Equal(200, (int)response.StatusCode);
        return System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(AdminTestHost.Ct));
    }
}
