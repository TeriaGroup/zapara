using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class BrowserAccountServiceTests
{
    private static readonly Guid Family = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ExportId = new("22222222-2222-4222-8222-222222222222");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProofIsBoundToPurposeSingleUseAndClearedWhenTheFamilyChanges()
    {
        var family = Family; var exports = 0;
        using var handler = new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/web-api/session") return BrowserApiClientTests.Session(family);
            if (path == "/web-api/account/reauthenticate")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Ct));
                return Json(new { proofToken = new string('b', 43), purpose = body.RootElement.GetProperty("purpose").GetString(), expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
            }
            Assert.Equal("/web-api/account/exports", path); exports++;
            Assert.Equal(Family.ToString(), request.Headers.GetValues("X-Zapara-Family").Single());
            return Json(new ExportJobResponse(ExportId, "ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        await api.RefreshSessionAsync(Ct);
        await using var service = new BrowserAccountService(api, new AccountBridge());
        await service.PasswordProofAsync("password-canary", "export", Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAccountAsync(Ct));
        Assert.True(service.HasProof("export"));
        await service.RequestExportAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestExportAsync(Ct));
        Assert.Equal(1, exports);
        await service.PasswordProofAsync("password-canary", "export", Ct);
        family = Guid.NewGuid(); await api.RefreshSessionAsync(Ct);
        Assert.False(service.HasProof("export"));
        Assert.Null(service.Export);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestExportAsync(Ct));
        Assert.Equal(1, exports);
    }

    [Fact]
    public async Task OAuthPersistsOnlyRoutingMetadataAndConsumesLinkProofBeforeNavigation()
    {
        var bridge = new AccountBridge();
        using var handler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") return BrowserApiClientTests.Session(Family);
            if (request.RequestUri.AbsolutePath.EndsWith("reauthenticate")) return Json(new { proofToken = new string('b', 43), purpose = "link:vk", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
            Assert.Equal("/web-api/auth/external/vk/start", request.RequestUri.AbsolutePath);
            using var content = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Ct));
            Assert.Equal(new string('b', 43), content.RootElement.GetProperty("proofToken").GetString());
            return Json(new { transactionId = ExportId, authorizeUrl = "https://id.vk.ru/authorize?state=provider-state", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage); await api.RefreshSessionAsync(Ct);
        await using var service = new BrowserAccountService(api, bridge);
        await service.PasswordProofAsync("password-canary", "link:vk", Ct);
        await service.StartOAuthAsync("vk", "link", ct: Ct);
        Assert.False(service.HasProof("link:vk"));
        Assert.Equal("link:vk", bridge.Pending!.Action);
        Assert.Equal(Family, bridge.Pending.FamilyId);
        var persisted = JsonSerializer.Serialize(bridge.Pending);
        Assert.DoesNotContain("password-canary", persisted);
        Assert.DoesNotContain(new string('b', 43), persisted);
        Assert.DoesNotContain("authorize", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://id.vk.ru/authorize?state=provider-state", bridge.Navigated);
    }

    [Theory]
    [InlineData("vk", "https://attacker.invalid/authorize")]
    [InlineData("vk", "https://id.vk.ru.evil.invalid/authorize")]
    [InlineData("vk", "http://id.vk.ru/authorize")]
    [InlineData("yandex", "https://oauth.yandex.ru/other")]
    [InlineData("yandex", "https://user@oauth.yandex.ru/authorize")]
    public void OAuthNavigationCannotUseUntrustedServerResponse(string provider, string url)
        => Assert.Throws<InvalidOperationException>(() => BrowserAccountService.ValidateProviderUrl(provider, url));

    [Fact]
    public async Task ExportDownloadsBytesOnlyAfterTheReadyReceiptAndUsesAGeneratedFilename()
    {
        var bridge = new AccountBridge();
        using var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/web-api/session") return Task.FromResult(BrowserApiClientTests.Session(Family));
            if (path.EndsWith("reauthenticate")) return Task.FromResult(Json(new { proofToken = new string('b', 43), purpose = "export", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }));
            if (path.EndsWith("exports")) return Task.FromResult(Json(new ExportJobResponse(ExportId, "ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1))));
            Assert.Equal("/web-api/account/exports/" + ExportId + "/download", path);
            return Task.FromResult(BrowserApiClientTests.Json("{\"profile\":{\"displayName\":\"Синтетическое имя\"}}"));
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage); await api.RefreshSessionAsync(Ct);
        await using var service = new BrowserAccountService(api, bridge);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadExportAsync(Ct));
        await service.PasswordProofAsync("password-canary", "export", Ct);
        await service.RequestExportAsync(Ct); await service.DownloadExportAsync(Ct);
        Assert.Equal("zapara-export-" + ExportId + ".json", bridge.Filename);
        Assert.Contains("Синтетическое имя", Encoding.UTF8.GetString(bridge.Download!));
    }

    [Fact]
    public async Task RestoredProviderProofNeverCrossesFamilyBoundary()
    {
        var bridge = new AccountBridge { Pending = new(ExportId, "delete_account", Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10)) };
        var requests = new List<string>();
        using var handler = new Handler(request => { requests.Add(request.RequestUri!.AbsolutePath); return Task.FromResult(BrowserApiClientTests.Session(Family)); });
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage); await api.RefreshSessionAsync(Ct);
        await using var service = new BrowserAccountService(api, bridge);
        var error = await Assert.ThrowsAsync<BrowserApiException>(() => service.ResumeOAuthAsync(Ct));
        Assert.Equal("account_changed", error.Code);
        Assert.Null(bridge.Pending); Assert.All(requests, path => Assert.Equal("/web-api/session", path));
    }

    [Fact]
    public async Task SettingFirstPasswordRefreshesIntoGuestBecauseTheServerRevokesAllFamilies()
    {
        var live = true;
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session")
                return Task.FromResult(live ? BrowserApiClientTests.Session(Family) : Json(new BrowserSession(false, null, null, new string('a', 43), new(true))));
            if (request.RequestUri.AbsolutePath.EndsWith("reauthenticate"))
                return Task.FromResult(Json(new { proofToken = new string('b', 43), purpose = "set_password", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }));
            if (request.RequestUri.AbsolutePath.EndsWith("password/set"))
            { live = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"code\":\"invalid_session\"}") });
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage); await api.RefreshSessionAsync(Ct);
        await using var service = new BrowserAccountService(api, new AccountBridge());
        await service.PasswordProofAsync("test-password", "set_password", Ct);
        await service.SetPasswordAsync("new-test-password", Ct);
        Assert.False(api.Session.Authenticated);
        Assert.False(service.HasProof("set_password"));
    }
    private static HttpResponseMessage Json<T>(T value) => BrowserApiClientTests.Json(JsonSerializer.Serialize(value, AccountJson.CreateOptions()));
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
    private sealed class AccountBridge : IJSRuntime, IJSObjectReference
    {
        internal BrowserOAuthPending? Pending;
        internal string? Navigated, Filename;
        internal byte[]? Download;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "savePending") Pending = (BrowserOAuthPending)args![0]!;
            if (identifier == "readPending") return ValueTask.FromResult((TValue)(object?)Pending!);
            if (identifier == "clearPending") Pending = null;
            if (identifier == "navigateProvider") Navigated = (string)args![1]!;
            if (identifier == "download") { Download = (byte[])args![0]!; Filename = (string)args[1]!; }
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
