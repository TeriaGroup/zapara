using System.Net.Http.Headers;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

internal sealed class SyncApiTestHost : IAsyncDisposable
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal WebApplicationFactory<Program> Factory { get; }
    internal HttpClient Client { get; }
    internal ConcurrentQueue<string> Logs { get; } = new();
    internal int Callbacks => Factory.Services.GetRequiredService<ObservedUnitOfWork>().Callbacks;
    internal AccountService Accounts => Factory.Services.GetRequiredService<AccountService>();
    internal SyncApiTestHost(SyncPostgresFixture db, AccountClock? clock = null,
        Dictionary<string, string?>? overrides = null, PostgresFixture? timetable = null, bool observe = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Accounts.Schema,
            ["Sync:Enabled"] = "true", ["Sync:Schema"] = db.Schema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        };
        if (overrides is not null) foreach (var pair in overrides) settings[pair.Key] = pair.Value;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", settings["Accounts:Enabled"]);
            builder.UseSetting("Sync:Enabled", settings["Sync:Enabled"]);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(Logs)));
            if (observe) builder.ConfigureServices(services =>
            {
                services.AddSingleton(provider => new ObservedUnitOfWork(provider.GetRequiredService<AccountService>()));
                services.RemoveAll<IAccountUnitOfWork>();
                services.AddSingleton<IAccountUnitOfWork>(provider => provider.GetRequiredService<ObservedUnitOfWork>());
            });
            if (timetable is not null) builder.ConfigureServices(services =>
            {
                services.RemoveAll<Zapara.Server.Timetable.TimetableConfiguration>();
                services.AddSingleton(timetable.Configuration);
            });
            if (clock is not null) builder.ConfigureServices(services =>
            { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); });
        });
        try { Client = Factory.CreateClient(); }
        catch { Factory.Dispose(); throw; }
    }
    internal async Task<byte[]> Send(string method, string path, int status, string? token = null, byte[]? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/sync" + path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await Client.SendAsync(request, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }
    internal async Task<T> Get<T>(string path, string token)
        => SyncJson.Parse<T>(await Send("GET", path, 200, token));
    internal async Task<byte[]> Mutate(SyncMutation mutation, string token, int status = 200)
        => await Send("POST", "/mutations", status, token, SyncJson.Serialize(mutation));
    public async ValueTask DisposeAsync() { Client.Dispose(); await Factory.DisposeAsync(); }

    private sealed class CaptureProvider(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(logs);
        public void Dispose() { }
    }
    private sealed class ObservedUnitOfWork(IAccountUnitOfWork inner) : IAccountUnitOfWork
    {
        private int callbacks;
        internal int Callbacks => Volatile.Read(ref callbacks);
        public Task<T> ExecuteAsync<T>(string bearer, Func<TrustedAccountContext, CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => inner.ExecuteAsync(bearer, (context, token) =>
            {
                Interlocked.Increment(ref callbacks);
                return operation(context, token);
            }, ct);
    }
    private sealed class CaptureLogger(ConcurrentQueue<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => logs.Enqueue(formatter(state, exception) + exception?.ToString());
    }
}
