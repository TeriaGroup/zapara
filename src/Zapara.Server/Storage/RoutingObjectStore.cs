using Npgsql;
using Zapara.Server.Accounts;
using Zapara.Server.Operator;
using Zapara.Server.Social;

namespace Zapara.Server.Storage;

public sealed class RoutingObjectStore : IObjectStore, IContentArchive
{
    private readonly IConfiguration configuration;
    private readonly Func<S3Target, HttpClient>? clients;
    private readonly string root;

    public RoutingObjectStore(IConfiguration configuration, Func<S3Target, HttpClient>? clients = null)
    {
        this.configuration = configuration;
        this.clients = clients;
        root = Root(configuration);
    }

    public bool RemoteConfigured() => Target() is not null;

    public void Put(string key, byte[] bytes)
    {
        var path = Safe(key);
        var s3 = Target();
        if (s3 is not null)
        {
            Open(s3).Put(key, bytes);
            return;
        }
        Directory.CreateDirectory(root);
        File.WriteAllBytes(path, bytes);
    }

    public byte[]? Get(string key)
    {
        var s3 = Target();
        if (s3 is not null)
        {
            try
            {
                var remote = Open(s3).Get(key);
                if (remote is not null) return remote;
            }
            catch (HttpRequestException)
            {
                // The configured bucket did not answer. A local copy can still be read.
            }
        }
        var path = Safe(key);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public void Delete(string key)
    {
        var s3 = Target();
        if (s3 is not null) Open(s3).Delete(key);
        var path = Safe(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private S3ObjectStore Open(S3Target target) => new(target, clients?.Invoke(target));

    private S3Target? Target()
    {
        var stored = Read();
        if (!OperatorConfig.StorageReady(stored, key => configuration[key])) return null;
        return new(
            OperatorConfig.Pick(stored, "s3_endpoint", configuration["S3:Endpoint"])!,
            OperatorConfig.Pick(stored, "s3_region", configuration["S3:Region"])!,
            OperatorConfig.Pick(stored, "s3_bucket", configuration["S3:Bucket"])!,
            OperatorConfig.Pick(stored, "s3_access_key", configuration["S3:AccessKey"])!,
            OperatorConfig.Pick(stored, "s3_secret", configuration["S3:Secret"])!);
    }

    private IReadOnlyDictionary<string, string> Read()
    {
        try
        {
            var raw = configuration.GetConnectionString("Accounts") ?? configuration.GetConnectionString("Timetable");
            if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, string>();
            using var connection = new NpgsqlConnection(raw);
            return OperatorSettings.ReadAll(connection, OperatorSettings.Schema(configuration));
        }
        catch (Exception) { return new Dictionary<string, string>(); }
    }

    private string Safe(string key)
    {
        if (key.Length is < 8 or > 80 || key.Contains("..", StringComparison.Ordinal) || key.IndexOfAny(['/', '\\']) >= 0)
            throw new InvalidOperationException("Недопустимое имя файла.");
        var path = Path.GetFullPath(Path.Combine(root, key));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недопустимое имя файла.");
        return path;
    }

    private static string Root(IConfiguration configuration)
    {
        var configured = configuration["Social:MediaRoot"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zapara", "social-media")
            : configured.Trim();
        return Path.GetFullPath(path);
    }
}
