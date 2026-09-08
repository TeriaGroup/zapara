using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

internal sealed class CommunityApiTestHost : IAsyncDisposable
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal WebApplication App { get; }
    internal HttpClient Client { get; }
    internal AccountService Accounts => App.Services.GetRequiredService<AccountService>();
    private CommunityApiTestHost(WebApplication app, HttpClient client) => (App, Client) = (app, client);

    internal static async Task<CommunityApiTestHost> StartAsync(CommunityPostgresFixture db, bool mapCommunities = true,
        AccountClock? clock = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Accounts.Schema,
            ["Communities:Enabled"] = mapCommunities ? "true" : "false", ["Communities:Schema"] = db.Schema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<TimeProvider>(clock ?? TimeProvider.System);
        builder.Services.AddAccounts(builder.Configuration);
        builder.Services.AddCommunities(builder.Configuration);
        var app = builder.Build();
        app.MapAccounts();
        app.MapCommunities();
        try
        {
            await app.StartAsync(Ct);
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single().TrimEnd('/') + "/") };
            return new(app, client);
        }
        catch { await app.DisposeAsync(); throw; }
    }

    internal async Task<byte[]> Send(string method, string path, int status, string? token = null, byte[]? body = null, bool store = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/communities" + path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await Client.SendAsync(request, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        if (store) Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }
    internal async Task<T> Get<T>(string path, string token, int status = 200)
        => CommunityJson.Parse<T>(await Send("GET", path, status, token));
    internal async Task<JsonElement> Problem(string method, string path, int status, string code, string? token = null, byte[]? body = null)
    {
        var bytes = await Send(method, path, status, token, body);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement.Clone();
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Matches("[А-Яа-я]", root.GetProperty("title").GetString()!);
        return root;
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync(CancellationToken.None);
        await App.DisposeAsync();
    }
}
