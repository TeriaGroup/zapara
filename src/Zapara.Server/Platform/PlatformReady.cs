using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zapara.Server.Accounts;
using Zapara.Server.Admin;
using Zapara.Server.Communities;
using Zapara.Server.Sync;

namespace Zapara.Server.Platform;

public sealed class PlatformReady
{
    public const string Present = "present";
    public const string Missing = "missing";
    public const string Disabled = "disabled";

    private static readonly string[] Names = ["timetable", "accounts", "sync", "communities", "admin"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Func<CancellationToken, Task<string>> timetable;
    private readonly Func<CancellationToken, Task<string>> accounts;
    private readonly Func<CancellationToken, Task<string>> sync;
    private readonly Func<CancellationToken, Task<string>> communities;
    private readonly Func<CancellationToken, Task<string>> admin;

    public PlatformReady(
        Func<CancellationToken, Task<string>> timetable,
        Func<CancellationToken, Task<string>> accounts,
        Func<CancellationToken, Task<string>> sync,
        Func<CancellationToken, Task<string>> communities,
        Func<CancellationToken, Task<string>> admin)
    {
        this.timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));
        this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        this.sync = sync ?? throw new ArgumentNullException(nameof(sync));
        this.communities = communities ?? throw new ArgumentNullException(nameof(communities));
        this.admin = admin ?? throw new ArgumentNullException(nameof(admin));
    }

    public static PlatformReady FromConfiguration(
        IConfiguration configuration,
        Func<CancellationToken, Task<bool>> timetable,
        Func<CancellationToken, Task<bool>> accounts,
        Func<CancellationToken, Task<bool>> sync,
        Func<CancellationToken, Task<bool>> communities,
        Func<CancellationToken, Task<bool>> admin)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(communities);
        ArgumentNullException.ThrowIfNull(admin);

        // Validate enablement flags up front so invalid values fail closed without probing.
        var accountsEnabled = AccountsConfiguration.IsEnabled(configuration);
        var syncEnabled = SyncConfiguration.IsEnabled(configuration);
        var communitiesEnabled = CommunitiesConfiguration.IsEnabled(configuration);
        var adminEnabled = AdminConfiguration.IsEnabled(configuration);

        return new PlatformReady(
            ct => Map(timetable, enabled: true, ct),
            ct => Map(accounts, accountsEnabled, ct),
            ct => Map(sync, syncEnabled, ct),
            ct => Map(communities, communitiesEnabled, ct),
            ct => Map(admin, adminEnabled, ct));
    }

    public async Task<PlatformReadyReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, probe) in new (string Name, Func<CancellationToken, Task<string>> Probe)[]
                 {
                     ("timetable", timetable),
                     ("accounts", accounts),
                     ("sync", sync),
                     ("communities", communities),
                     ("admin", admin)
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            modules[name] = Normalize(await probe(cancellationToken));
        }

        return new PlatformReadyReport(new ReadOnlyDictionary<string, string>(modules));
    }

    public static IResult ToResult(PlatformReadyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var modules = Names.ToDictionary(name => name, name => report.Modules[name], StringComparer.Ordinal);
        if (report.IsReady)
            return new FrozenJson(JsonSerializer.Serialize(new { status = "ready", modules }, Json), 200, "application/json");
        return new FrozenJson(
            JsonSerializer.Serialize(new { title = "Платформа не готова", status = 503, code = "platform_not_ready", modules }, Json),
            503,
            "application/problem+json");
    }

    public override string ToString() => "PlatformReady { [REDACTED] }";

    private static async Task<string> Map(Func<CancellationToken, Task<bool>> probe, bool enabled, CancellationToken ct)
    {
        if (!enabled) return Disabled;
        return await probe(ct) ? Present : Missing;
    }

    private static string Normalize(string status) => status switch
    {
        Present or Missing or Disabled => status,
        _ => throw new ArgumentException("Недопустимый статус модуля.")
    };

    private sealed class FrozenJson(string body, int status, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.StatusCode = status;
            httpContext.Response.ContentType = contentType;
            httpContext.Response.ContentLength = bytes.Length;
            await httpContext.Response.Body.WriteAsync(bytes, httpContext.RequestAborted);
        }
    }
}

public sealed class PlatformReadyReport
{
    public PlatformReadyReport(IReadOnlyDictionary<string, string> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var name in new[] { "timetable", "accounts", "sync", "communities", "admin" })
        {
            if (!modules.TryGetValue(name, out var status) || status is not (PlatformReady.Present or PlatformReady.Missing or PlatformReady.Disabled))
                throw new ArgumentException("Недопустимый отчёт готовности.");
        }

        Modules = modules;
    }

    public IReadOnlyDictionary<string, string> Modules { get; }
    public bool IsReady => Modules.Values.All(status => status != PlatformReady.Missing);
    public override string ToString() => "PlatformReadyReport { [REDACTED] }";
}
