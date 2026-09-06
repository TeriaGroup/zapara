using System.Net;

namespace Vograph.Desktop.Tests;

/// <summary>Scripted HttpMessageHandler: tests decide the response per request and inspect what was sent.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Bytes(byte[] body, HttpStatusCode code = HttpStatusCode.OK, DateTimeOffset? lastModified = null)
    {
        var resp = new HttpResponseMessage(code) { Content = new ByteArrayContent(body) };
        if (lastModified is { } lm) resp.Content.Headers.LastModified = lm;
        return resp;
    }
}
