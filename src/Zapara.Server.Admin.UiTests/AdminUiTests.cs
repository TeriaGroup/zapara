using System.Net;
using Xunit;
using Zapara.Server.Admin;

namespace Zapara.Server.Admin.UiTests;

public sealed class AdminUiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Razor_login_is_not_the_operator_surface()
    {
        await using var db = await AdminUiPostgres.CreateAsync();
        await using var host = await AdminUiHost.StartAsync(db);
        var admin = await db.RegisterAsync("ui.adm." + db.Hex[..8]);
        Assert.Equal(AdminBootstrapOutcome.Committed, await AdminBootstrap.RunAsync(db.DataSource, db.Admin, admin.UserId, Ct));
        using var login = await host.Client.GetAsync("/Admin/Login", Ct);
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        var body = await login.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Вход в админку", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Создать сообщество", body, StringComparison.Ordinal);
        using var home = await host.Client.GetAsync("/Admin", Ct);
        Assert.Equal(HttpStatusCode.NotFound, home.StatusCode);
    }
}
