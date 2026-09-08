using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;
using Zapara.Server.Accounts.ExternalProviders;

namespace Zapara.Server.Tests;

public sealed class OAuthFlowTests
{
    [Fact]
    public async Task Start_binds_distinct_server_PKCE_without_creating_account()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        using var adapter = new YandexIdAdapter(new("synthetic-client", "https://example.invalid/registered/yandex"));
        var registry = new ExternalProviderRegistry([adapter], new Dictionary<string, string> { ["yandex"] = "https://example.invalid/registered/yandex" });
        var service = new ExternalAuthService(db.DataSource, db.Configuration, TimeProvider.System, registry);
        var challenge = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var request = new ExternalStartRequest("login", challenge, "S256", new DeviceInput(Guid.NewGuid(), "Тест", "windows"), new("windows", 45001));
        var error = await Record.ExceptionAsync(async () =>
        {
            var started = await service.StartAsync("yandex", request, ct: TestContext.Current.CancellationToken);
            var query = QueryHelpers.ParseQuery(new Uri(started.AuthorizeUrl).Query);
            Assert.NotEqual(challenge, query["code_challenge"].ToString());
            Assert.Equal("https://example.invalid/registered/yandex", query["redirect_uri"].ToString());
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users"));
        });
        Assert.Null(error);
    }
}
