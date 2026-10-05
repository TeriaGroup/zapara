using Microsoft.AspNetCore.WebUtilities;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Zapara.Server.Accounts;
using Zapara.Server.Accounts.ExternalProviders;
using Xunit;

namespace Zapara.Server.Tests;

internal sealed class OAuthTestHarness : IAsyncDisposable
{
    internal AccountsPostgresFixture Db { get; }
    internal AccountClock Clock { get; } = new();
    internal OAuthHandler Handler { get; } = new(false);
    private readonly HttpClient http;
    private readonly YandexIdAdapter adapter;
    internal ExternalProviderRegistry Registry { get; }
    internal ExternalAuthService Service { get; }
    internal AccountService Accounts { get; }
    internal const string Callback = "https://example.invalid/registered/yandex";
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
    private OAuthTestHarness(AccountsPostgresFixture db)
    {
        Db = db;
        http = new(Handler);
        adapter = new(new("synthetic-client", Callback), http);
        Registry = new([adapter], new Dictionary<string, string> { ["yandex"] = Callback });
        Service = new(db.DataSource, db.Configuration, Clock, Registry);
        Accounts = new(db.DataSource, db.Configuration, Clock);
    }
    internal static async Task<OAuthTestHarness> Create() => new(await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true));
    internal async Task<(ExternalStartResponse Start, string Verifier, string State)> Start(string purpose = "login", string? access = null, string? proof = null, string? scope = null)
    {
        var verifier = ExternalSecrets.Random();
        var start = await Service.StartAsync("yandex", new(purpose, WebEncoders.Base64UrlEncode(ExternalSecrets.Hash(verifier)), "S256",
            new(Guid.NewGuid(), "Тест", "windows"), new("windows", 45001), proof, scope), access, Ct);
        return (start, verifier, QueryHelpers.ParseQuery(new Uri(start.AuthorizeUrl).Query)["state"].ToString());
    }
    internal async Task<ExternalExchangeRequest> Complete((ExternalStartResponse Start, string Verifier, string State) pending)
    {
        var uri = await Service.CallbackAsync("yandex", new(Callback), pending.State, "synthetic-code", ct: Ct);
        return new(pending.Start.TransactionId, pending.Verifier, QueryHelpers.ParseQuery(uri.Query)["handoffCode"].ToString());
    }
    internal async Task<ExternalExchangeResponse> Flow(string purpose = "login", string? access = null, string? proof = null, string? scope = null)
        => await Service.ExchangeAsync(await Complete(await Start(purpose, access, proof, scope)), access, Ct);
    public async ValueTask DisposeAsync() { adapter.Dispose(); http.Dispose(); Handler.Dispose(); await Db.DisposeAsync(); }
}
