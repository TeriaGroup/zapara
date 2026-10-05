using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Zapara.Server.Accounts.ExternalProviders;

internal sealed record ExternalProviderTestsRequest(string Method, Uri Uri, string? Authorization, string Body);

internal sealed class ExternalProviderTestsHandler : HttpMessageHandler
{
    internal List<ExternalProviderTestsRequest> Requests { get; } = [];
    internal List<HttpRequestMessage> OriginalRequests { get; } = [];
    internal List<ExternalProviderTestsContent> Contents { get; } = [];
    internal Func<int, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }
    internal string TokenJson { get; set; } = "{\"access_token\":\"ACCESS_CANARY\"}";
    internal string UserJson { get; set; } = "{\"id\":\"opaque-subject\",\"client_id\":\"example-id\",\"display_name\":\"Test\"}";
    internal HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new ExternalProviderTestsContent(Encoding.UTF8.GetBytes(json));
        Contents.Add(content);
        return new(status) { Content = content };
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        OriginalRequests.Add(request);
        Requests.Add(new(request.Method.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return Respond is null ? Response(Requests.Count == 1 ? TokenJson : UserJson) : await Respond(Requests.Count, ct);
    }
}

internal sealed class ExternalProviderTestsContent(byte[] bytes) : HttpContent
{
    internal bool Disposed { get; private set; }
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

internal static class ExternalProviderTestsSupport
{
    internal const string Callback = "https://example.invalid/backend/callback%20test";
    internal const string Secret = "SECRET_CANARY";
    internal const string Code = "CODE_CANARY+&=/";
    internal static readonly ProviderState State = new(new string('s', 32) + "_-A");
    internal static readonly CodeVerifier Verifier = new(new string('v', 43));
    internal static readonly CodeChallenge Challenge = new(WebEncoders.Base64UrlEncode(
        System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(new string('v', 43)))));
    internal static IExternalProviderAdapter Adapter(bool vk, HttpClient http, TimeProvider? clock = null, bool secret = true)
        => vk ? new VkIdAdapter(new("example-id", Callback, secret ? Secret : null), http, clock)
            : new YandexIdAdapter(new("example-id", Callback, secret ? Secret : null), http, clock);
    internal static Task<VerifiedExternalIdentity> Exchange(IExternalProviderAdapter adapter, bool vk)
        => ExchangeWithCancellation(adapter, vk, TestContext.Current.CancellationToken);
    internal static Task<VerifiedExternalIdentity> ExchangeWithCancellation(IExternalProviderAdapter adapter, bool vk, CancellationToken ct)
        => adapter.ExchangeIdentityAsync(Code, State, Verifier, vk ? "device+&=/" : null, ct);
    internal static Dictionary<string, string> Form(string text) => QueryHelpers.ParseQuery(text)
        .ToDictionary(x => x.Key, x => x.Value.ToString());
}
