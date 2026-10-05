using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

internal sealed class AdminTestHost : IAsyncDisposable
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal ConcurrentQueue<string> Logs { get; } = new();
    internal WebApplicationFactory<Program> Factory { get; }
    internal HttpClient Client { get; }

    internal AdminTestHost(AdminPostgresFixture db, bool adminEnabled = true, bool mapAdmin = true,
        Dictionary<string, string?>? overrides = null, PostgresFixture? timetable = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Accounts.Schema,
            ["Communities:Enabled"] = "true", ["Communities:Schema"] = db.Communities.Schema,
            ["Admin:Enabled"] = adminEnabled && mapAdmin ? "true" : "false",
            ["Admin:Schema"] = db.Schema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        };
        if (overrides is not null) foreach (var pair in overrides) configuration[pair.Key] = pair.Value;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", configuration["Accounts:Enabled"]);
            builder.UseSetting("Communities:Enabled", configuration["Communities:Enabled"]);
            builder.UseSetting("Admin:Enabled", configuration["Admin:Enabled"]);
            builder.UseSetting("https_port", "443");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(Logs)));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(db.Clock);
                if (timetable is not null)
                {
                    services.RemoveAll<TimetableConfiguration>();
                    services.AddSingleton(timetable.Configuration);
                }
            });
        });
        try
        {
            Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        }
        catch { Factory.Dispose(); throw; }
    }

    internal AccountService Accounts => Factory.Services.GetRequiredService<AccountService>();

    internal async Task<string> GetHtml(string path, int status = 200)
    {
        using var response = await Client.GetAsync(path, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        if (path.StartsWith("/Admin", StringComparison.Ordinal))
            Assert.True(response.Headers.CacheControl?.NoStore);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
    }

    internal static string Antiforgery(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        if (!match.Success)
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Нет anti-forgery токена.");
        return match.Groups[1].Value;
    }

    internal async Task<HttpResponseMessage> PostForm(string path, IReadOnlyDictionary<string, string> fields, string? token)
    {
        var pairs = fields.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();
        if (token is not null) pairs.Add(new("__RequestVerificationToken", token));
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(pairs)
        };
        return await Client.SendAsync(request, Ct);
    }

    internal async Task<string> LoginAsync(string username, string password, int status = 302)
    {
        var html = await GetHtml("/Admin/Login");
        using var response = await PostForm("/Admin/Login", new Dictionary<string, string>
        {
            ["Username"] = username, ["Password"] = password
        }, Antiforgery(html));
        Assert.Equal(status, (int)response.StatusCode);
        return string.Join('\n', response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []);
    }

    internal async Task<UserResponse> RegisterAsync(string username)
        => await Accounts.RegisterAsync(new RegisterRequest(username, AccountTestSupport.Password), Ct);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }

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
