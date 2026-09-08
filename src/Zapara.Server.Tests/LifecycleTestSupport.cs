using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Communities;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

internal sealed class LifecycleHarness : IAsyncDisposable
{
    internal const int SchemaVersion = 4;
    internal AccountsPostgresFixture Accounts { get; }
    internal string? SyncSchema { get; }
    internal string? CommunitiesSchema { get; }
    internal SyncConfiguration? Sync { get; }
    internal CommunitiesConfiguration? Communities { get; }

    private LifecycleHarness(AccountsPostgresFixture accounts, string? syncSchema, SyncConfiguration? sync,
        string? communitiesSchema, CommunitiesConfiguration? communities)
    {
        Accounts = accounts;
        SyncSchema = syncSchema;
        Sync = sync;
        CommunitiesSchema = communitiesSchema;
        Communities = communities;
    }

    internal static async Task<LifecycleHarness> CreateAsync(Action<string> receipt, bool sync = false, bool communities = false)
    {
        var accounts = await AccountsPostgresFixture.CreateAsync(receipt, true, SchemaVersion);
        string? syncSchema = null;
        string? comSchema = null;
        try
        {
            SyncConfiguration? syncConfig = null;
            if (sync)
            {
                syncSchema = "sync_test_" + Guid.NewGuid().ToString("N");
                await accounts.ExecuteAsync($"CREATE SCHEMA \"{syncSchema}\"");
                receipt($"CREATE {syncSchema}");
                syncConfig = SyncPostgresFixture.Options(syncSchema, accounts.Schema);
                await new SyncMigrations(accounts.DataSource, syncConfig).EnsureAsync(TestContext.Current.CancellationToken);
            }
            CommunitiesConfiguration? comConfig = null;
            if (communities)
            {
                comSchema = "com_test_" + Guid.NewGuid().ToString("N");
                await accounts.ExecuteAsync($"CREATE SCHEMA \"{comSchema}\"");
                receipt($"CREATE {comSchema}");
                comConfig = CommunityPostgresFixture.Options(comSchema, accounts.Schema);
                await new CommunitiesMigrations(accounts.DataSource, comConfig).EnsureAsync(TestContext.Current.CancellationToken);
            }
            return new(accounts, syncSchema, syncConfig, comSchema, comConfig);
        }
        catch
        {
            await DropOwned(accounts, syncSchema, comSchema);
            await accounts.DisposeAsync();
            throw;
        }
    }

    internal Dictionary<string, string?> HostSettings()
    {
        var settings = new Dictionary<string, string?>();
        if (Sync is not null)
        {
            settings["Sync:Enabled"] = "true";
            settings["Sync:Schema"] = Sync.Schema;
        }
        if (Communities is not null)
        {
            settings["Communities:Enabled"] = "true";
            settings["Communities:Schema"] = Communities.Schema;
        }
        return settings;
    }

    public async ValueTask DisposeAsync()
    {
        try { await DropOwned(Accounts, SyncSchema, CommunitiesSchema); }
        finally { await Accounts.DisposeAsync(); }
    }

    private static async Task DropOwned(AccountsPostgresFixture accounts, string? syncSchema, string? communitiesSchema)
    {
        if (syncSchema is not null)
        {
            await accounts.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{syncSchema}\" CASCADE");
            Assert.Equal(0L, await accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{syncSchema}'"));
        }
        if (communitiesSchema is not null)
        {
            await accounts.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{communitiesSchema}\" CASCADE");
            Assert.Equal(0L, await accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{communitiesSchema}'"));
        }
    }
}

internal sealed class LifecycleApiHost : IAsyncDisposable
{
    internal ConcurrentQueue<string> Logs { get; } = new();
    internal WebApplicationFactory<Program> Factory { get; }
    internal HttpClient Client { get; }
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static JsonSerializerOptions Json => AccountJson.CreateOptions();

    internal LifecycleApiHost(LifecycleHarness harness, string environment = "Testing", TimeProvider? clock = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true",
            ["Accounts:Schema"] = harness.Accounts.Schema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        };
        foreach (var pair in harness.HostSettings()) configuration[pair.Key] = pair.Value;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Accounts:Enabled", "true");
            builder.UseSetting("Accounts:Schema", configuration["Accounts:Schema"]);
            if (configuration.TryGetValue("Sync:Enabled", out var syncEnabled) && syncEnabled is not null)
            {
                builder.UseSetting("Sync:Enabled", syncEnabled);
                builder.UseSetting("Sync:Schema", configuration["Sync:Schema"]);
            }
            if (configuration.TryGetValue("Communities:Enabled", out var comEnabled) && comEnabled is not null)
            {
                builder.UseSetting("Communities:Enabled", comEnabled);
                builder.UseSetting("Communities:Schema", configuration["Communities:Schema"]);
            }
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(Logs)));
            if (clock is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(clock);
                });
            }
        });
        try { Client = Factory.CreateClient(); }
        catch { Factory.Dispose(); throw; }
    }

    internal async Task<JsonElement> Send(string method, string path, int status, object? body = null,
        string? bearer = null, string? code = null, bool json = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1" + path);
        if (bearer is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync(Ct);
        if (status == 204) { Assert.Empty(text); return default; }
        if (!json) return default;
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement.Clone();
        if (code is not null)
        {
            ApiTestFactory.Keys(root, "title", "status", "code");
            Assert.Equal(status, root.GetProperty("status").GetInt32());
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.Matches("[А-Яа-я]", root.GetProperty("title").GetString()!);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(!text.Contains(AccountApiTestHost.Password), "Error credential canaries must be absent.");
            if (bearer is not null) Assert.True(!text.Contains(bearer), "Error bearer canary must be absent.");
        }
        return root;
    }

    internal async Task<(JsonElement Body, string? FileName, string? Media)> Download(string path, int status, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1" + path);
        if (bearer is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        using var response = await Client.SendAsync(request, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        var media = response.Content.Headers.ContentType?.MediaType;
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(text);
        return (document.RootElement.Clone(), fileName?.Trim('"'), media);
    }

    internal Task<JsonElement> Register(string username = "synthetic")
        => Send("POST", "/auth/register", 201, new RegisterRequest(username, AccountApiTestHost.Password));
    internal async Task<SessionResponse> Login(string username = "synthetic", string password = AccountApiTestHost.Password)
        => (await Send("POST", "/auth/login", 200, AccountApiTestHost.LoginBody(username, password))).Deserialize<SessionResponse>(Json)!;
    internal async Task<string> Proof(SessionResponse session, string purpose)
    {
        var proof = await Send("POST", "/account/reauthenticate", 200,
            new PasswordProofRequest(AccountApiTestHost.Password, purpose), session.AccessToken);
        return proof.GetProperty("proofToken").GetString()!;
    }

    public async ValueTask DisposeAsync() { Client.Dispose(); await Factory.DisposeAsync(); }

    private sealed class CaptureProvider(ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
        public void Dispose() { }
    }
    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => messages.Enqueue(formatter(state, exception) + (exception?.ToString() ?? ""));
    }
}
