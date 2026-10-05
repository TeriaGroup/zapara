using System.Net;
using System.Text;
using System.Text.Json;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserApiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Family = new("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task Browser_mutations_bind_the_current_family_and_csrf_without_any_bearer()
    {
        using var handler = new ScriptedHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") return Session(Family);
            Assert.Equal("/web-api/sync/mutations", request.RequestUri.AbsolutePath);
            Assert.Equal(new string('a', 43), request.Headers.GetValues("X-Zapara-CSRF").Single());
            Assert.Equal(Family.ToString("D"), request.Headers.GetValues("X-Zapara-Family").Single());
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.Contains("привет", await request.Content!.ReadAsStringAsync(Ct));
            return Json("{\"status\":\"ok\"}");
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        await api.RefreshSessionAsync(Ct);

        var result = await api.SendAsync<JsonElement>(HttpMethod.Post, "sync/mutations", new { text = "привет" }, ct: Ct);

        Assert.Equal("ok", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_response_started_before_an_account_switch_is_not_delivered_to_the_new_account()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var family = Family;
        using var handler = new ScriptedHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") return Session(family);
            started.TrySetResult(); await release.Task.WaitAsync(Ct);
            return Json("{\"privateText\":\"старый аккаунт\"}");
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);
        await api.RefreshSessionAsync(Ct);
        var old = api.GetAsync<JsonElement>("account/me", Ct);
        await started.Task.WaitAsync(Ct);
        family = Guid.NewGuid();
        await api.RefreshSessionAsync(Ct);
        release.SetResult();

        var error = await Assert.ThrowsAsync<BrowserApiException>(() => old);

        Assert.Equal("account_changed", error.Code);
    }

    [Theory]
    [InlineData("https://outside.invalid/private")]
    [InlineData("../api/v1/account/me")]
    [InlineData("/account/me")]
    public async Task Browser_client_does_not_send_sessions_to_untrusted_paths(string path)
    {
        using var handler = new ScriptedHandler(_ => throw new Xunit.Sdk.XunitException("No request may leave the client."));
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        var api = new BrowserApiClient(http, storage);

        var error = await Assert.ThrowsAsync<BrowserApiException>(() => api.GetAsync<JsonElement>(path, Ct));

        Assert.Equal("invalid_request", error.Code);
    }

    internal static HttpResponseMessage Session(Guid family) => Json(JsonSerializer.Serialize(new
    {
        authenticated = true, user = new { userId = family, username = "student", displayName = "Студент", createdAt = "2026-09-01T00:00:00Z" },
        familyId = family, csrfToken = new string('a', 43), capabilities = new { password = true, vk = false, yandex = false, registration = true, recovery = false }
    }));
    internal static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
