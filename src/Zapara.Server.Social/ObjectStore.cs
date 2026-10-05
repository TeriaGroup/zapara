namespace Zapara.Server.Social;

public interface IObjectStore
{
    void Put(string key, byte[] bytes);
    byte[]? Get(string key);
    void Delete(string key);
}

public sealed class MemoryObjectStore : IObjectStore
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

    public void Put(string key, byte[] bytes) => files[key] = bytes.ToArray();
    public byte[]? Get(string key) => files.TryGetValue(key, out var bytes) ? bytes.ToArray() : null;
    public void Delete(string key) => files.Remove(key);
    public bool Contains(string key) => files.ContainsKey(key);
}

public readonly record struct QuotaState(long UserUsed, long UserLimit, long GroupUsed, long GroupLimit, bool HasGroup = true);

public readonly record struct QuotaDecision(bool Allowed, string? Code, string? Reason);

public static class QuotaRules
{
    public const long DefaultGroupBytes = 1_073_741_824;
    public const long DefaultUserBytes = 524_288_000;

    public static QuotaDecision Decide(QuotaState state, long incoming)
    {
        if (incoming < 0) return new(false, "invalid_request", "Файл не принят.");
        if (state.UserUsed > state.UserLimit || incoming > state.UserLimit - state.UserUsed)
            return new(false, "quota_user", "Превышен лимит трафика студента.");
        if (state.HasGroup && (state.GroupUsed > state.GroupLimit || incoming > state.GroupLimit - state.GroupUsed))
            return new(false, "quota_group", "Превышен лимит трафика группы.");
        return new(true, null, null);
    }
}

public readonly record struct UploadResult(bool Stored, string? Reason, byte[]? Bytes);

public static class StudyGroupScope
{
    public static string? Choose(string? requested, IReadOnlyList<string> groups)
    {
        if (groups.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            foreach (var group in groups)
                if (string.Equals(group, requested, StringComparison.Ordinal)) return group;
        }
        return groups[0];
    }
}

public sealed class UploadIntake(IObjectStore store)
{
    public UploadResult Store(QuotaState quotas, string key, byte[] bytes)
    {
        var decision = QuotaRules.Decide(quotas, bytes.LongLength);
        if (!decision.Allowed) return new(false, decision.Reason, null);
        store.Put(key, bytes);
        return new(true, null, store.Get(key));
    }
}
