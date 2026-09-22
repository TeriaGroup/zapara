using System.Net;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    [Fact]
    public async Task BrowserDeviceUsesV2AndMissingRouteNegotiatesLegacyOnce()
    {
        var paths = new List<string>();
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(paths.Count == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(new DevicesResponse([], null)));
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        await client.ListDevicesAsync(Token("za_"), ct: Ct);
        await client.ListDevicesAsync(Token("za_"), ct: Ct);
        Assert.Equal(["/api/v2/account/devices", "/api/v1/account/devices", "/api/v1/account/devices"], paths);
    }

    [Fact]
    public async Task BrowserPlatformIsReadAndServiceFailureNeverFallsBack()
    {
        var device = new DeviceResponse(FamilyId, DeviceId, "Браузер", "web", Now, Now, Now.AddDays(30), false);
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(Json(new DevicesResponse([device], null))) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        Assert.Equal("web", Assert.Single((await client.ListDevicesAsync(Token("za_"), ct: Ct)).Devices).Platform);
        handler.Send = (_, _) => Task.FromResult(Json(new { status = 503, code = "db_unavailable" }, HttpStatusCode.ServiceUnavailable));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.ListDevicesAsync(Token("za_"), ct: Ct));
        Assert.Equal(AccountClientFailure.DbUnavailable, error.Failure);
        Assert.Equal(2, handler.Calls);
    }
}
