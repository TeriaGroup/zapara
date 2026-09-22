using System.Text.Json;
using Microsoft.JSInterop;

namespace Zapara.Web.Services;

public sealed class BrowserStorageConflictException() : InvalidOperationException("Данные изменились в другой вкладке. Повторите действие.");
public sealed class BrowserStorageResetException() : InvalidOperationException("Этот локальный профиль был очищен. Старое действие отменено; откройте профиль заново.");

public sealed partial class BrowserStorage
{
    private const long MaximumStorageRevision = 9_007_199_254_740_991;

    /// <summary>The transform may run again after a competing tab commits. It must be pure; mint operation IDs before calling.</summary>
    public async Task<WebProfile> UpdateProfileAsync(string owner, Func<WebProfile, WebProfile> transform, CancellationToken ct = default, long expectedResetEpoch = 0)
    {
        ValidateOwner(owner);
        ArgumentNullException.ThrowIfNull(transform);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var bridge = await Module;
            var raw = await bridge.InvokeAsync<string?>("read", ct, "profiles", owner);
            var latest = raw is null ? new WebProfile { Owner = owner } : JsonSerializer.Deserialize<WebProfile>(raw, Json)
                ?? throw new InvalidDataException("Сохранённый профиль не удалось прочитать.");
            ValidateProfile(latest, owner);
            EnsureResetEpoch(latest, expectedResetEpoch);
            var expected = latest.StorageRevision;
            ct.ThrowIfCancellationRequested();
            var transformed = transform(CloneProfile(latest)) ?? throw new InvalidDataException("Изменение вернуло пустой профиль.");
            ValidateProfile(transformed, owner);
            EnsureResetEpoch(transformed, expectedResetEpoch);
            var candidate = CloneProfile(transformed);
            candidate.StorageRevision = NextRevision(expected);
            ct.ThrowIfCancellationRequested();
            // Once sent, await the transaction outcome even if ct changes. Cancelling the JS await cannot undo its commit.
            if (await bridge.InvokeAsync<bool>("compareExchangeProfile", owner, expected, JsonSerializer.Serialize(candidate, Json)))
                return candidate;
        }
        throw new BrowserStorageConflictException();
    }

    /// <summary>Atomically replaces profile and public cache if its disk revision still matches. The caller's objects remain unchanged.</summary>
    public async Task<bool> TryCommitSnapshotAsync(string owner, long expectedRevision, WebProfile profile, PublicCache cache, CancellationToken ct = default, long expectedResetEpoch = 0)
    {
        ValidateOwner(owner);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(cache);
        ValidateProfile(profile, owner);
        EnsureResetEpoch(profile, expectedResetEpoch);
        var candidate = CloneProfile(profile);
        candidate.StorageRevision = NextRevision(expectedRevision);
        ct.ThrowIfCancellationRequested();
        var bridge = await Module;
        ct.ThrowIfCancellationRequested();
        return await bridge.InvokeAsync<bool>("compareExchangeProfileSnapshot", owner, expectedRevision,
            JsonSerializer.Serialize(candidate, Json), JsonSerializer.Serialize(cache, Json));
    }

    private static WebProfile CloneProfile(WebProfile value) => JsonSerializer.Deserialize<WebProfile>(JsonSerializer.Serialize(value, Json), Json)
        ?? throw new InvalidDataException("Не удалось подготовить профиль к сохранению.");

    private static void ValidateOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 2048) throw new ArgumentException("Некорректный владелец профиля.", nameof(owner));
    }

    private static void ValidateProfile(WebProfile profile, string owner)
    {
        if (!string.Equals(profile.Owner, owner, StringComparison.Ordinal))
            throw new InvalidDataException("Владелец сохранённого профиля не совпадает. Данные не изменены.");
        if (profile.StorageRevision is < 0 or > MaximumStorageRevision || profile.ResetEpoch is < 0 or > MaximumStorageRevision || profile.Records is null || profile.Outbox is null)
            throw new InvalidDataException("Некорректный сохранённый профиль. Данные не изменены.");
    }

    public static void EnsureResetEpoch(WebProfile profile, long expected)
    {
        if (expected < 0 || profile.ResetEpoch != expected) throw new BrowserStorageResetException();
    }

    private static long NextRevision(long current) => current is >= 0 and < MaximumStorageRevision ? current + 1
        : throw new InvalidDataException("Недопустимая версия сохранённого профиля.");
}
