using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;

namespace Zapara.Server.Admin.UiTests;

internal sealed class AdminUiHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly X509Certificate2 certificate;
    internal string Origin { get; }
    internal HttpClient Client { get; }
    internal AccountService Accounts => app.Services.GetRequiredService<AccountService>();
    internal CommunityService Communities => app.Services.GetRequiredService<CommunityService>();

    private AdminUiHost(WebApplication app, X509Certificate2 certificate, string origin, HttpClient client)
    {
        this.app = app;
        this.certificate = certificate;
        Origin = origin;
        Client = client;
    }

    internal static async Task<AdminUiHost> StartAsync(AdminUiPostgres db)
    {
        var contentRoot = FindContentRoot();
        var certificate = CreateCertificate();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Zapara.Server",
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot"),
            EnvironmentName = "Testing"
        });
        builder.Configuration.AddInMemoryCollection(db.HostSettings);
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Npgsql", LogLevel.None);
        builder.WebHost.UseKestrel();
        builder.WebHost.UseSetting("https_port", "443");
        builder.WebHost.UseSetting("Accounts:Enabled", "true");
        builder.WebHost.UseSetting("Communities:Enabled", "true");
        builder.WebHost.UseSetting("Admin:Enabled", "true");
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAccounts(builder.Configuration);
        builder.Services.AddExternalAuth(builder.Configuration);
        builder.Services.AddCommunities(builder.Configuration);
        builder.Services.AddAdmin(builder.Configuration);
        builder.Services.AddRazorPages().AddApplicationPart(typeof(Zapara.Server.Program).Assembly);
        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = 500;
            return Task.CompletedTask;
        }));
        app.MapAccounts();
        app.MapExternalAuth();
        app.MapCommunities();
        app.MapAdmin();
        try
        {
            await app.StartAsync(Ct);
            var origin = BoundOrigin(app);
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            })
            {
                BaseAddress = new Uri(origin + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            return new AdminUiHost(app, certificate, origin, client);
        }
        catch
        {
            await app.DisposeAsync();
            certificate.Dispose();
            throw;
        }
    }

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

    internal async Task LoginAsync(string username, string password, int status = 302)
    {
        var html = await GetHtml("/Admin/Login");
        using var response = await PostForm("/Admin/Login", new Dictionary<string, string>
        {
            ["Username"] = username, ["Password"] = password
        }, Antiforgery(html));
        Assert.Equal(status, (int)response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
        certificate.Dispose();
    }

    private static string BoundOrigin(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
            ?? app.Urls;
        var origin = addresses.Select(value => value.TrimEnd('/')).FirstOrDefault(value => value.StartsWith("https://", StringComparison.Ordinal));
        if (string.IsNullOrEmpty(origin)) throw new InvalidOperationException("Kestrel не привязал HTTPS.");
        return origin;
    }

    private static string FindContentRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Zapara.Server");
            if (File.Exists(Path.Combine(candidate, "Zapara.Server.csproj")) &&
                Directory.Exists(Path.Combine(candidate, "wwwroot")))
                return candidate;
        }
        throw new InvalidOperationException("Не найден каталог Zapara.Server.");
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var loaded = new X509Certificate2(created.Export(X509ContentType.Pfx, "ui"), "ui", X509KeyStorageFlags.Exportable);
        if (!loaded.HasPrivateKey) throw new InvalidOperationException("Нет закрытого ключа сертификата.");
        return loaded;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
