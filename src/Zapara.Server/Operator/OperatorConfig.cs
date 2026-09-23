namespace Zapara.Server.Operator;

public static class OperatorConfig
{
    public const long DefaultGroupBytes = 1_073_741_824;
    public const long DefaultUserBytes = 524_288_000;
    public static readonly string[] SecretKeys = ["vk_secret", "yandex_secret", "s3_secret"];

    public static Dictionary<string, string> Apply(IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string?> patch)
    {
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
        foreach (var (key, value) in patch)
        {
            if (value is null) continue;
            if (IsSecret(key) && value.Length == 0) continue;
            next[key] = value;
        }
        if (!next.ContainsKey("quota_group_bytes")) next["quota_group_bytes"] = DefaultGroupBytes.ToString();
        if (!next.ContainsKey("quota_user_bytes")) next["quota_user_bytes"] = DefaultUserBytes.ToString();
        return next;
    }

    public static Dictionary<string, string> Public(IReadOnlyDictionary<string, string> stored)
    {
        var view = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in stored)
            view[key] = IsSecret(key) ? (value.Length == 0 ? "" : "configured") : value;
        return view;
    }

    public static long Quota(IReadOnlyDictionary<string, string> stored, string key, long fallback)
        => stored.TryGetValue(key, out var raw) && long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;

    public static string? Pick(IReadOnlyDictionary<string, string> stored, string key, string? configured)
        => stored.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : configured;

    public static bool Enabled(IReadOnlyDictionary<string, string> stored, string key, bool configured)
        => stored.TryGetValue(key, out var value) ? bool.TryParse(value, out var parsed) && parsed : configured;

    public static bool ProviderAvailable(IReadOnlyDictionary<string, string> stored, string name, bool configured)
    {
        var enabledKey = name + "_enabled";
        var clientKey = name + "_client_id";
        var callbackKey = name + "_callback";
        if (!stored.ContainsKey(enabledKey) && !stored.ContainsKey(clientKey) && !stored.ContainsKey(callbackKey))
            return configured;
        return Enabled(stored, enabledKey, false)
            && !string.IsNullOrWhiteSpace(Pick(stored, clientKey, null))
            && !string.IsNullOrWhiteSpace(Pick(stored, callbackKey, null));
    }

    public static bool StorageReady(IReadOnlyDictionary<string, string> stored, Func<string, string?>? configured)
    {
        string? Value(string key, string configKey) => Pick(stored, key, configured?.Invoke(configKey));
        return !string.IsNullOrWhiteSpace(Value("s3_endpoint", "S3:Endpoint"))
            && !string.IsNullOrWhiteSpace(Value("s3_region", "S3:Region"))
            && !string.IsNullOrWhiteSpace(Value("s3_bucket", "S3:Bucket"))
            && !string.IsNullOrWhiteSpace(Value("s3_access_key", "S3:AccessKey"))
            && !string.IsNullOrWhiteSpace(Value("s3_secret", "S3:Secret"));
    }

    private static bool IsSecret(string key)
    {
        foreach (var secret in SecretKeys)
            if (secret == key) return true;
        return false;
    }
}
