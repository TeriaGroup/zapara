using System.Net;
using Microsoft.Playwright;
using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Admin;
using Zapara.Server.Communities;

namespace Zapara.Server.Admin.UiTests;

[Collection("admin-ui")]
public sealed class AdminUiTests
{
    private readonly AdminUiGate gate;
    public AdminUiTests(AdminUiGate gate) => this.gate = gate;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = 120_000)]
    public async Task Login_logout_csrf_and_anonymous_accounts_are_rejected()
    {
        await using var db = await AdminUiPostgres.CreateAsync();
        await using var host = await AdminUiHost.StartAsync(db);
        var suffix = db.Hex[..8];
        var adminName = "ui.adm." + suffix;
        var admin = await db.RegisterAsync(adminName);
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.DataSource, db.Admin, admin.UserId, Ct));

        var login = await host.GetHtml("/Admin/Login");
        Assert.Contains("Вход в админку", login, StringComparison.Ordinal);
        Assert.Contains("Имя пользователя", login, StringComparison.Ordinal);
        Assert.Contains("Пароль", login, StringComparison.Ordinal);
        Assert.Contains("Войти", login, StringComparison.Ordinal);
        Assert.DoesNotContain(">Login<", login, StringComparison.Ordinal);
        using (var missing = await host.PostForm("/Admin/Login", new Dictionary<string, string>
        {
            ["Username"] = adminName, ["Password"] = AdminUiPostgres.Password
        }, token: null))
            Assert.Equal(400, (int)missing.StatusCode);

        using (var anonymousGet = await host.Client.GetAsync("/Admin/Accounts", Ct))
        {
            Assert.True((int)anonymousGet.StatusCode is 302 or 401 or 403);
            if (anonymousGet.StatusCode == HttpStatusCode.Found)
                Assert.Contains("/Admin/Login", anonymousGet.Headers.Location?.ToString(), StringComparison.Ordinal);
        }
        using (var anonymousPost = await host.PostForm("/Admin/Accounts?handler=Disable", new Dictionary<string, string>
        {
            ["UserId"] = admin.UserId.ToString("D"), ["CurrentPassword"] = AdminUiPostgres.Password
        }, token: null))
            Assert.True((int)anonymousPost.StatusCode is 400 or 401 or 403 or 302);

        await host.LoginAsync(adminName, AdminUiPostgres.Password);
        var home = await host.GetHtml("/Admin");
        Assert.Contains("Администрирование", home, StringComparison.Ordinal);
        Assert.Contains("Выйти", home, StringComparison.Ordinal);
        using (var csrf = await host.PostForm("/Admin/Accounts?handler=Disable", new Dictionary<string, string>
        {
            ["UserId"] = admin.UserId.ToString("D"), ["CurrentPassword"] = AdminUiPostgres.Password
        }, token: null))
            Assert.Equal(400, (int)csrf.StatusCode);

        using var logout = await host.PostForm("/Admin/Logout", new Dictionary<string, string>(), AdminUiHost.Antiforgery(home));
        Assert.Equal(302, (int)logout.StatusCode);
        using var after = await host.Client.GetAsync("/Admin", Ct);
        Assert.Equal(HttpStatusCode.Found, after.StatusCode);
        Assert.Contains("/Admin/Login", after.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Staff_revoke_forbids_publish_and_audit_lists_moderation()
    {
        await using var db = await AdminUiPostgres.CreateAsync();
        await using var host = await AdminUiHost.StartAsync(db);
        var suffix = db.Hex[..8];
        var adminName = "ui.adm." + suffix;
        var staffName = "ui.stf." + suffix;
        var admin = await db.RegisterAsync(adminName);
        var staff = await db.RegisterAsync(staffName);
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.DataSource, db.Admin, admin.UserId, Ct));
        await host.LoginAsync(adminName, AdminUiPostgres.Password);

        var communities = await host.GetHtml("/Admin/Communities");
        Assert.Contains("Создать сообщество", communities, StringComparison.Ordinal);
        using (var created = await host.PostForm("/Admin/Communities?handler=Create", new Dictionary<string, string>
        {
            ["Name"] = "О3313", ["Description"] = "Учебная группа"
        }, AdminUiHost.Antiforgery(communities)))
            Assert.Equal(302, (int)created.StatusCode);
        var communityId = await db.CommunityIdAsync("О3313");

        var staffPage = await host.GetHtml("/Admin/Staff");
        Assert.Contains("Назначить персонал", staffPage, StringComparison.Ordinal);
        using (var assigned = await host.PostForm("/Admin/Staff", new Dictionary<string, string>
        {
            ["CommunityId"] = communityId.ToString("D"),
            ["UserId"] = staff.UserId.ToString("D"),
            ["Role"] = "headman",
            ["CurrentPassword"] = AdminUiPostgres.Password
        }, AdminUiHost.Antiforgery(staffPage)))
            Assert.Equal(302, (int)assigned.StatusCode);
        Assert.Equal("headman", await db.ScalarAsync<string>(
            $"SELECT role FROM {db.QuotedCommunities}.memberships WHERE user_id=@p0", "p0", staff.UserId));

        var session = await db.LoginAccountAsync(staffName);
        var published = await db.CommunityService.PublishHomeworkAsync(session.AccessToken, communityId,
            new HomeworkUpsert("Спам", "Удалить", 0), Ct);
        Assert.Equal(1, published.Revision);

        var content = await host.GetHtml("/Admin/Content");
        Assert.Contains("Модерация", content, StringComparison.Ordinal);
        using (var moderated = await host.PostForm("/Admin/Content", new Dictionary<string, string>
        {
            ["Kind"] = "shared_homework",
            ["ObjectId"] = published.HomeworkId.ToString("D"),
            ["CommunityId"] = communityId.ToString("D"),
            ["CurrentPassword"] = AdminUiPostgres.Password
        }, AdminUiHost.Antiforgery(content)))
            Assert.Equal(302, (int)moderated.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedCommunities}.shared_homework"));

        await db.RevokeStaffAsync(communityId, staff.UserId);
        var denied = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            db.CommunityService.PublishHomeworkAsync(session.AccessToken, communityId, new HomeworkUpsert("Ещё", "Нет", 0), Ct));
        Assert.Equal(403, denied.Status);
        Assert.Equal("forbidden", denied.Code);

        var audit = await host.GetHtml("/Admin/Audit");
        Assert.Contains("Журнал", audit, StringComparison.Ordinal);
        Assert.Contains("community_created", audit, StringComparison.Ordinal);
        Assert.Contains("staff_assigned", audit, StringComparison.Ordinal);
        Assert.Contains("content_moderated", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminUiPostgres.Password, audit, StringComparison.Ordinal);
        Assert.DoesNotContain("za_", audit, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Keyboard_login_and_viewports_keep_russian_layout_usable()
    {
        gate.RequirePlaywright();
        await using var db = await AdminUiPostgres.CreateAsync();
        await using var host = await AdminUiHost.StartAsync(db);
        var suffix = db.Hex[..8];
        var adminName = "ui.key." + suffix;
        var admin = await db.RegisterAsync(adminName);
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.DataSource, db.Admin, admin.UserId, Ct));

        await using var context = await gate.Browser!.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            Locale = "ru-RU",
            ViewportSize = new() { Width = 1920, Height = 1080 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.Origin + "/Admin/Login");
        await AssertLoginUsable(page, 1920);
        await TabToAsync(page, "Username");
        await page.Keyboard.TypeAsync(adminName);
        await page.Keyboard.PressAsync("Tab");
        var active = await page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.id");
        Assert.Equal("Password", active);
        await page.Keyboard.TypeAsync(AdminUiPostgres.Password);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync("**/Admin");
        var home = await page.ContentAsync();
        Assert.Contains("Администрирование", home, StringComparison.Ordinal);
        Assert.Contains("Выйти", home, StringComparison.Ordinal);
        Assert.DoesNotContain(">Login<", home, StringComparison.Ordinal);

        await page.GetByRole(AriaRole.Button, new() { Name = "Выйти" }).ClickAsync();
        await page.WaitForURLAsync("**/Admin/Login");
        Assert.Contains("Вход в админку", await page.ContentAsync(), StringComparison.Ordinal);

        await page.SetViewportSizeAsync(375, 667);
        await page.GotoAsync(host.Origin + "/Admin/Login");
        await AssertLoginUsable(page, 375);
    }

    [Fact(Timeout = 180_000)]
    public async Task Browser_staff_assignment_and_audit_are_visible()
    {
        gate.RequirePlaywright();
        await using var db = await AdminUiPostgres.CreateAsync();
        await using var host = await AdminUiHost.StartAsync(db);
        var suffix = db.Hex[..8];
        var adminName = "ui.brw." + suffix;
        var staffName = "ui.bws." + suffix;
        var admin = await db.RegisterAsync(adminName);
        var staff = await db.RegisterAsync(staffName);
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.DataSource, db.Admin, admin.UserId, Ct));

        await using var context = await gate.Browser!.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            Locale = "ru-RU",
            ViewportSize = new() { Width = 1920, Height = 1080 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.Origin + "/Admin/Login");
        await page.GetByLabel("Имя пользователя").FillAsync(adminName);
        await page.GetByLabel("Пароль").FillAsync(AdminUiPostgres.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Войти" }).ClickAsync();
        await page.WaitForURLAsync("**/Admin");

        await page.GetByLabel("Разделы админки").GetByRole(AriaRole.Link, new() { Name = "Сообщества" }).ClickAsync();
        await page.GetByLabel("Название").FillAsync("О3313");
        await page.GetByLabel("Описание").FillAsync("Учебная группа");
        await page.GetByRole(AriaRole.Button, new() { Name = "Создать" }).ClickAsync();
        await page.WaitForURLAsync("**/Admin/Communities");
        var communityId = await db.CommunityIdAsync("О3313");

        await page.GetByLabel("Разделы админки").GetByRole(AriaRole.Link, new() { Name = "Персонал" }).ClickAsync();
        await page.GetByLabel("Идентификатор пользователя").FillAsync(staff.UserId.ToString("D"));
        await page.GetByLabel("Подтверждение пароля").FillAsync(AdminUiPostgres.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Назначить" }).ClickAsync();
        await page.WaitForURLAsync("**/Admin/Staff");
        Assert.Equal("headman", await db.ScalarAsync<string>(
            $"SELECT role FROM {db.QuotedCommunities}.memberships WHERE user_id=@p0", "p0", staff.UserId));

        var session = await db.LoginAccountAsync(staffName);
        await db.CommunityService.PublishHomeworkAsync(session.AccessToken, communityId, new HomeworkUpsert("Спам", "Удалить", 0), Ct);
        await db.RevokeStaffAsync(communityId, staff.UserId);
        var denied = await Assert.ThrowsAsync<CommunityServiceException>(() =>
            db.CommunityService.PublishHomeworkAsync(session.AccessToken, communityId, new HomeworkUpsert("Ещё", "Нет", 0), Ct));
        Assert.Equal(403, denied.Status);

        await page.GetByLabel("Разделы админки").GetByRole(AriaRole.Link, new() { Name = "Журнал" }).ClickAsync();
        await page.WaitForURLAsync("**/Admin/Audit");
        var audit = await page.ContentAsync();
        Assert.Contains("Журнал", audit, StringComparison.Ordinal);
        Assert.Contains("staff_assigned", audit, StringComparison.Ordinal);
        Assert.Contains("community_created", audit, StringComparison.Ordinal);
    }

    private static async Task TabToAsync(IPage page, string id)
    {
        for (var i = 0; i < 12; i++)
        {
            var current = await page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.id");
            if (current == id) return;
            await page.Keyboard.PressAsync("Tab");
        }
        Assert.Fail("Tab не дошёл до " + id);
    }

    private static async Task AssertLoginUsable(IPage page, int width)
    {
        var heading = page.GetByRole(AriaRole.Heading, new() { Name = "Вход в админку" });
        var user = page.GetByLabel("Имя пользователя");
        var password = page.GetByLabel("Пароль");
        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Войти" });
        Assert.True(await heading.IsVisibleAsync());
        Assert.True(await user.IsVisibleAsync());
        Assert.True(await password.IsVisibleAsync());
        Assert.True(await submit.IsVisibleAsync());
        var box = await submit.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Width > 0 && box.Height > 0);
        Assert.True(box.X >= 0 && box.X + box.Width <= width + 1);
        var family = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).fontFamily");
        Assert.Contains("Inter", family, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ru", await page.EvaluateAsync<string>("() => document.documentElement.lang"));
    }
}
