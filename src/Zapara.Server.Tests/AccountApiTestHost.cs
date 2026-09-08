using System.Collections.Concurrent;
using System.Net;
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
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

internal sealed class AccountApiTestHost : IAsyncDisposable
{
    internal const string Password = "Synthetic password canary 987!";
    internal const string NewPassword = "Changed synthetic canary 654!";
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static JsonSerializerOptions Json => AccountJson.CreateOptions();
    internal ConcurrentQueue<string> Logs { get; } = new();
    internal WebApplicationFactory<Program> Factory { get; }
    internal HttpClient Client { get; }

    internal AccountApiTestHost(AccountsPostgresFixture db, string environment = "Testing",
        Dictionary<string, string?>? overrides = null, TimeProvider? clock = null, PostgresFixture? timetable = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Schema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        };
        if (overrides is not null) foreach (var pair in overrides) configuration[pair.Key] = pair.Value;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Minimal-host service registration runs before deferred ConfigureAppConfiguration.
            // Enablement must also be visible in the initial host settings, without process globals.
            builder.UseSetting("Accounts:Enabled", configuration["Accounts:Enabled"]);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(Logs)));
            builder.ConfigureServices(services =>
            {
                if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
                if (timetable is not null)
                {
                    services.RemoveAll<TimetableConfiguration>();
                    services.AddSingleton(timetable.Configuration);
                }
            });
        });
        try { Client = Factory.CreateClient(); }
        catch { Factory.Dispose(); throw; }
    }

    internal async Task<JsonElement> Send(string method, string path, int status, object? body = null,
        string? bearer = null, string? raw = null, string? code = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1" + path);
        if (bearer is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        if (raw is not null || body is not null)
            request.Content = new StringContent(raw ?? JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync(Ct);
        if (status == 204) { Assert.Empty(text); return default; }
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement.Clone();
        if (code is not null)
        {
            ApiTestFactory.Keys(root, "title", "status", "code");
            Assert.Equal(status, root.GetProperty("status").GetInt32());
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.Matches("[А-Яа-я]", root.GetProperty("title").GetString()!);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(!text.Contains(Password) && !text.Contains(NewPassword), "Error credential canaries must be absent.");
            if (bearer is not null) Assert.True(!text.Contains(bearer), "Error bearer canary must be absent.");
        }
        return root;
    }

    internal Task<JsonElement> Register(string username = "synthetic")
        => Send("POST", "/auth/register", 201, new RegisterRequest(username, Password));
    internal async Task<SessionResponse> Login(string username = "synthetic", string password = Password)
        => (await Send("POST", "/auth/login", 200, LoginBody(username, password))).Deserialize<SessionResponse>(Json)!;
    internal static LoginRequest LoginBody(string username = "synthetic", string password = Password)
        => new(username, password, new DeviceInput(Guid.NewGuid(), "Test device", "windows"));
    internal async Task<SessionResponse> Refresh(string token)
        => (await Send("POST", "/auth/refresh", 200, new { refreshToken = token })).Deserialize<SessionResponse>(Json)!;
    internal Task<JsonElement> InvalidAccess(string token)
        => Send("GET", "/account/me", 401, bearer: token, code: "invalid_session");

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
