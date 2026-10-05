using System.Net;
using System.Text.Json;
using Xunit;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class BrowserApiConflictTests
{
    [Fact]
    public async Task Invalid_constructor_contract_and_nonobject_error_body_are_safe_API_errors()
    {
        using var http = new HttpClient(new MalformedHandler()) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        await using var api = new BrowserApiClient(http, storage);
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<BrowserApiException>(() => api.GetAsync<CheckedReply>("typed", TestContext.Current.CancellationToken))).Code);
        Assert.Equal("request_failed", (await Assert.ThrowsAsync<BrowserApiException>(() => api.GetAsync<JsonElement>("error", TestContext.Current.CancellationToken))).Code);
    }
    public sealed class CheckedReply
    {
        [System.Text.Json.Serialization.JsonConstructor]
        public CheckedReply(int count) { if (count < 0) throw new ArgumentException("private-response-sentinel"); Count = count; }
        public int Count { get; }
    }
    private sealed class MalformedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var error = request.RequestUri!.AbsolutePath.EndsWith("/error");
            var response = BrowserApiClientTests.Json(error ? "[]" : "{\"count\":-1}");
            if (error) response.StatusCode = HttpStatusCode.BadRequest;
            return Task.FromResult(response);
        }
    }
    [Theory]
    [InlineData("session_changed")]
    [InlineData("csrf_invalid")]
    [InlineData("revision_conflict")]
    public async Task Mutation_conflict_without_typed_metadata_is_an_API_failure(string code)
    {
        using var http = new HttpClient(new Handler(code)) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(new MemoryBrowser());
        await using var api = new BrowserApiClient(http, storage);
        await api.RefreshSessionAsync(TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<BrowserApiException>(() => api.SendAsync<JsonElement>(HttpMethod.Post,
            "sync/mutations", new { }, ct: TestContext.Current.CancellationToken));
        Assert.Equal(code, failure.Code);
        if (code == "session_changed") Assert.Contains("другой вкладке", failure.Message);
    }
    private sealed class Handler(string code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") return Task.FromResult(BrowserApiClientTests.Session(Guid.NewGuid()));
            var response = BrowserApiClientTests.Json(JsonSerializer.Serialize(new { code })); response.StatusCode = HttpStatusCode.Conflict;
            return Task.FromResult(response);
        }
    }
}
