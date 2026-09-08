using System.Net;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Admin;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class AdminPageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Screens_cover_community_staff_membership_account_content_and_audit()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        var admin = await host.RegisterAsync("plat.admin");
        var staff = await host.RegisterAsync("group.head");
        var member = await host.RegisterAsync("group.mem");
        var target = await host.RegisterAsync("acct.target");
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.Accounts.DataSource, db.Configuration, admin.UserId, Ct));
        await host.LoginAsync("plat.admin", Password);

        var communities = await host.GetHtml("/Admin/Communities");
        Assert.Contains("Создать сообщество", communities, StringComparison.Ordinal);
        Assert.Contains("Привязать группу", communities, StringComparison.Ordinal);
        using (var created = await host.PostForm("/Admin/Communities?handler=Create", new Dictionary<string, string>
        {
            ["Name"] = "О3313", ["Description"] = "Учебная группа"
        }, AdminTestHost.Antiforgery(communities)))
            Assert.Equal(302, (int)created.StatusCode);
        var communityId = await db.ScalarAsync<Guid>($"SELECT community_id FROM {db.Communities.QuotedSchema}.communities");
        var mapped = await host.GetHtml("/Admin/Communities");
        using (var map = await host.PostForm("/Admin/Communities?handler=Map", new Dictionary<string, string>
        {
            ["CommunityId"] = communityId.ToString("D"),
            ["GroupId"] = "O3313", ["GroupName"] = "О3313"
        }, AdminTestHost.Antiforgery(mapped)))
            Assert.Equal(302, (int)map.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.Communities.QuotedSchema}.memberships"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.Communities.QuotedSchema}.catalog_maps"));

        var staffPage = await host.GetHtml("/Admin/Staff");
        Assert.Contains("Назначить персонал", staffPage, StringComparison.Ordinal);
        using (var withoutProof = await host.PostForm("/Admin/Staff", new Dictionary<string, string>
        {
            ["CommunityId"] = communityId.ToString("D"),
            ["UserId"] = staff.UserId.ToString("D"),
            ["Role"] = "headman"
        }, AdminTestHost.Antiforgery(staffPage)))
        {
            Assert.Equal(200, (int)withoutProof.StatusCode);
            var body = System.Net.WebUtility.HtmlDecode(await withoutProof.Content.ReadAsStringAsync(Ct));
            Assert.Contains("Повторная аутентификация", body, StringComparison.Ordinal);
        }
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.Communities.QuotedSchema}.staff_assignments"));
        using (var assigned = await host.PostForm("/Admin/Staff", new Dictionary<string, string>
        {
            ["CommunityId"] = communityId.ToString("D"),
            ["UserId"] = staff.UserId.ToString("D"),
            ["Role"] = "headman",
            ["CurrentPassword"] = Password
        }, AdminTestHost.Antiforgery(await host.GetHtml("/Admin/Staff"))))
            Assert.Equal(302, (int)assigned.StatusCode);
        Assert.Equal("headman", await db.ScalarAsync<string>($"SELECT role FROM {db.Communities.QuotedSchema}.memberships WHERE user_id='{staff.UserId}'"));

        await db.ExecuteAsync($"""
            INSERT INTO {db.Communities.QuotedSchema}.join_requests(request_id,community_id,user_id,status,created_at)
            VALUES('{Guid.NewGuid()}','{communityId}','{member.UserId}','pending',TIMESTAMPTZ '2026-09-08 12:00:00+00')
            """);
        var joins = await host.GetHtml("/Admin/Memberships");
        Assert.Contains("Принять", joins, StringComparison.Ordinal);
        var requestId = await db.ScalarAsync<Guid>($"SELECT request_id FROM {db.Communities.QuotedSchema}.join_requests");
        using (var accepted = await host.PostForm("/Admin/Memberships?handler=Accept", new Dictionary<string, string>
        {
            ["CommunityId"] = communityId.ToString("D"),
            ["RequestId"] = requestId.ToString("D")
        }, AdminTestHost.Antiforgery(joins)))
            Assert.Equal(302, (int)accepted.StatusCode);
        Assert.Equal("member", await db.ScalarAsync<string>($"SELECT role FROM {db.Communities.QuotedSchema}.memberships WHERE user_id='{member.UserId}'"));
        Assert.Equal("headman", await db.ScalarAsync<string>($"SELECT role FROM {db.Communities.QuotedSchema}.memberships WHERE user_id='{staff.UserId}'"));

        var homeworkId = Guid.NewGuid();
        await db.ExecuteAsync($"""
            INSERT INTO {db.Communities.QuotedSchema}.shared_homework(homework_id,community_id,title,body,revision,created_by,created_at,updated_at)
            VALUES('{homeworkId}','{communityId}','Спам','Удалить',1,'{staff.UserId}',TIMESTAMPTZ '2026-09-08 12:00:00+00',TIMESTAMPTZ '2026-09-08 12:00:00+00')
            """);
        var content = await host.GetHtml("/Admin/Content");
        Assert.Contains("Модерация", content, StringComparison.Ordinal);
        using (var moderated = await host.PostForm("/Admin/Content", new Dictionary<string, string>
        {
            ["Kind"] = "shared_homework", ["ObjectId"] = homeworkId.ToString("D"),
            ["CommunityId"] = communityId.ToString("D"), ["CurrentPassword"] = Password
        }, AdminTestHost.Antiforgery(content)))
            Assert.Equal(302, (int)moderated.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.Communities.QuotedSchema}.shared_homework"));

        var session = await host.Accounts.LoginAsync(new LoginRequest("acct.target", Password, new DeviceInput(Guid.NewGuid(), "Тест", "windows")), Ct);
        var accounts = await host.GetHtml("/Admin/Accounts");
        Assert.Contains("Отключить", accounts, StringComparison.Ordinal);
        using (var disabled = await host.PostForm("/Admin/Accounts?handler=Disable", new Dictionary<string, string>
        {
            ["UserId"] = target.UserId.ToString("D"), ["CurrentPassword"] = Password
        }, AdminTestHost.Antiforgery(accounts)))
            Assert.Equal(302, (int)disabled.StatusCode);
        Assert.Equal("disabled", await db.ScalarAsync<string>($"SELECT status FROM {db.Accounts.QuotedSchema}.users WHERE user_id='{target.UserId}'"));
        await Assert.ThrowsAsync<Zapara.Server.Accounts.AccountServiceException>(() =>
            host.Accounts.GetMeAsync(session.AccessToken, Ct));

        var audit = await host.GetHtml("/Admin/Audit");
        Assert.Contains("Журнал", audit, StringComparison.Ordinal);
        Assert.Contains("community_created", audit, StringComparison.Ordinal);
        Assert.Contains("catalog_mapped", audit, StringComparison.Ordinal);
        Assert.Contains("staff_assigned", audit, StringComparison.Ordinal);
        Assert.Contains("join_accepted", audit, StringComparison.Ordinal);
        Assert.Contains("content_moderated", audit, StringComparison.Ordinal);
        Assert.Contains("account_disabled", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, audit, StringComparison.Ordinal);
        Assert.DoesNotContain("za_", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_admin_cookie_is_rejected_and_russian_nav_is_complete()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        await using var host = new AdminTestHost(db);
        var admin = await host.RegisterAsync("nav.admin");
        var other = await host.RegisterAsync("nav.other");
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.Accounts.DataSource, db.Configuration, admin.UserId, Ct));
        await host.LoginAsync("nav.admin", Password);
        var home = await host.GetHtml("/Admin");
        foreach (var label in new[] { "Сообщества", "Персонал", "Заявки", "Аккаунты", "Модерация", "Журнал", "Выйти" })
            Assert.Contains(label, home, StringComparison.Ordinal);
        using (var fonts = await host.Client.GetAsync("/fonts/inter_regular.ttf", Ct))
            Assert.Equal(HttpStatusCode.OK, fonts.StatusCode);
        using (var css = await host.Client.GetAsync("/css/admin.css", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, css.StatusCode);
            var text = await css.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("http://", text, StringComparison.Ordinal);
            Assert.DoesNotContain("https://", text, StringComparison.Ordinal);
            Assert.Contains("inter_regular", text, StringComparison.Ordinal);
        }
        using (var disable = await host.PostForm("/Admin/Accounts?handler=Disable", new Dictionary<string, string>
        {
            ["UserId"] = other.UserId.ToString("D"), ["CurrentPassword"] = Password
        }, AdminTestHost.Antiforgery(await host.GetHtml("/Admin/Accounts"))))
            Assert.Equal(302, (int)disable.StatusCode);
        await db.ExecuteAsync($"UPDATE {db.Accounts.QuotedSchema}.users SET status='disabled' WHERE user_id='{admin.UserId}'");
        using var rejected = await host.Client.GetAsync("/Admin", Ct);
        Assert.Equal(HttpStatusCode.Found, rejected.StatusCode);
        Assert.Contains("/Admin/Login", rejected.Headers.Location?.ToString(), StringComparison.Ordinal);
    }
}
