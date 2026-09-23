using System.Text.Json.Serialization;

namespace Vograph.Core.Services.Accounts;

public sealed partial class AccountHttpClient
{
    public Task<SupportThreadResponse[]> ListSupportAsync(string accessToken, CancellationToken ct = default)
        => SendAsync<SupportThreadResponse[]>(HttpMethod.Get, "support", null, accessToken, 200, ct);

    public Task<SupportThreadResponse> OpenSupportAsync(string accessToken, string subject, string body, CancellationToken ct = default)
        => SendAsync<SupportThreadResponse>(HttpMethod.Post, "support", new SupportOpenRequest(subject, body), accessToken, 200, ct);

    public Task<SupportThreadResponse> ContinueSupportAsync(string accessToken, Guid id, string body, CancellationToken ct = default)
        => SendAsync<SupportThreadResponse>(HttpMethod.Post, "support/" + id.ToString("D"), new SupportContinueRequest(body), accessToken, 200, ct);
}

public sealed record SupportOpenRequest(string Subject, string Body);
public sealed record SupportContinueRequest(string Body);

public sealed record SupportThreadResponse
{
    [JsonConstructor]
    public SupportThreadResponse(Guid id, string subject, IReadOnlyList<SupportLineResponse> messages)
        => (Id, Subject, Messages) = (id, subject, messages);
    public Guid Id { get; }
    public string Subject { get; }
    public IReadOnlyList<SupportLineResponse> Messages { get; }
}

public sealed record SupportLineResponse
{
    [JsonConstructor]
    public SupportLineResponse(string author, string body, DateTimeOffset at)
        => (Author, Body, At) = (author, body, at);
    public string Author { get; }
    public string Body { get; }
    public DateTimeOffset At { get; }
}
