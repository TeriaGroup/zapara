using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Zapara.Contracts.Communities;

namespace Zapara.Web.Services;

public sealed record CommunityReadCache(int Version, string Owner, string SessionGeneration, string Fence,
    DateTimeOffset SavedAt, CommunityResponse[] Communities, Dictionary<Guid, JoinRequestResponse?> OwnRequests,
    Dictionary<Guid, CommunityDetailCache> Details);
public sealed record CommunityDetailCache(CommunityResponse Community, DateTimeOffset SavedAt,
    HomeworkResponse[] Homework, Dictionary<Guid, CompletionResponse> Completions, AnnouncementResponse[] Announcements,
    PollResponse[] Polls, MemberResponse[] Members, MemberResponse[] Staff, JoinRequestResponse[] JoinRequests,
    Dictionary<Guid, VoteResponse?> OwnVotes, Dictionary<Guid, PollResultsResponse> Results);

public sealed partial class BrowserCommunityService
{
    private readonly BrowserStorage storage;
    private readonly WebAppState state;
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private readonly HashSet<string> invalidatedCaches = [];
    private CachePermit? displayedCache;
    private string observedOwner = "";
    private long observedProfileGeneration;
    public bool IsCached { get; private set; }
    public DateTimeOffset? CachedAt { get; private set; }
    public bool CanRead => !api.Transitioning && (api.Session.Authenticated || IsCached && displayedCache is { } cached && CacheScopeMatches(cached));
    public bool CanJoin => !IsCached && !Loading && !Busy && !RequiresRefresh && api.Available && api.Session.Authenticated && !api.Transitioning;
    private sealed record CachePermit(string Owner, string Stamp, string Fence);
    private static string CacheKey(string owner, string stamp) => "community-cache:" + Uri.EscapeDataString(owner) + ":" + Uri.EscapeDataString(stamp);
    private bool CacheScopeMatches(CachePermit permit) => !api.Transitioning && !state.IsGuest && state.ProfileKey == permit.Owner && api.ObservedSessionGeneration == permit.Stamp;

    private async Task<CachePermit?> BeginCacheReadAsync()
    {
        if (!state.Authenticated || api.Transitioning || api.ConfirmedSessionGeneration is not { Length: > 0 and <= 128 } stamp || stamp != api.ObservedSessionGeneration) return null;
        var ownerKey = state.ProfileKey;
        try
        {
            var fence = await storage.ReadAsync<string>("preferences", CacheKey(ownerKey, stamp) + ":fence") ?? "initial";
            var permit = new CachePermit(ownerKey, stamp, fence);
            return CacheScopeMatches(permit) ? permit : null;
        }
        catch (Exception e) when (CacheError(e)) { Notice = "Копия для чтения без сети недоступна: проверьте хранилище браузера."; return null; }
    }

    private async Task<CommunityReadCache?> ReadCacheAsync(CachePermit permit)
    {
        var key = CacheKey(permit.Owner, permit.Stamp);
        if (invalidatedCaches.Contains(key)) return null;
        var json = await storage.ReadAsync<JsonElement?>("preferences", key);
        if (json is null || json.Value.ValueKind == JsonValueKind.Null) return null;
        var text = json.Value.GetRawText();
        if (Encoding.UTF8.GetByteCount(text) > 2 * 1024 * 1024) return null;
        var value = JsonSerializer.Deserialize<CommunityReadCache>(text, BrowserStorage.Json);
        return value is not null && ValidCache(value, permit) ? value : null;
    }

    private static bool ValidCache(CommunityReadCache value, CachePermit permit)
    {
        if (value.Version != 1 || value.Owner != permit.Owner || value.SessionGeneration != permit.Stamp || value.Fence != permit.Fence ||
            value.SavedAt.Offset != TimeSpan.Zero || value.SavedAt < DateTimeOffset.UnixEpoch || value.Communities is null || value.OwnRequests is null || value.Details is null ||
            value.Communities.Length > 300 || value.OwnRequests.Count > 300 || value.Details.Count > 20 ||
            value.Communities.Any(c => c is null) || value.Communities.Select(c => c.CommunityId).Distinct().Count() != value.Communities.Length) return false;
        var ids = value.Communities.Select(c => c.CommunityId).ToHashSet();
        if (value.OwnRequests.Any(p => !ids.Contains(p.Key) || p.Value is { } r && (r.CommunityId != p.Key || !value.Owner.EndsWith("#" + r.UserId.ToString("D"), StringComparison.Ordinal)))) return false;
        foreach (var (id, detail) in value.Details)
        {
            if (detail is null || detail.Community is null || detail.Community.CommunityId != id || detail.Community.Role is null ||
                detail.SavedAt.Offset != TimeSpan.Zero || detail.SavedAt < DateTimeOffset.UnixEpoch || !value.Communities.Any(c => c.CommunityId == id && c.Role == detail.Community.Role) ||
                detail.Homework is null || detail.Announcements is null || detail.Polls is null || detail.Members is null || detail.Staff is null || detail.JoinRequests is null || detail.Completions is null || detail.OwnVotes is null || detail.Results is null ||
                detail.Homework.Length > 500 || detail.Announcements.Length > 500 || detail.Polls.Length > 200 || detail.Members.Length > 2000 || detail.Staff.Length > 2000 || detail.JoinRequests.Length > 2000 ||
                detail.Homework.Any(h => h is null || h.CommunityId != id) || detail.Announcements.Any(a => a is null || a.CommunityId != id) || detail.Polls.Any(p => p is null || p.CommunityId != id) ||
                detail.Members.Any(m => m is null) || detail.Staff.Any(m => m is null) || detail.JoinRequests.Any(r => r is null || r.CommunityId != id) ||
                detail.Community.Role == "member" && detail.JoinRequests.Length != 0) return false;
            var homework = detail.Homework.Select(h => h.HomeworkId).ToHashSet(); var polls = detail.Polls.ToDictionary(p => p.PollId);
            if (homework.Count != detail.Homework.Length || detail.Completions.Count != homework.Count || detail.Completions.Any(p => p.Value is null || p.Value.HomeworkId != p.Key || !homework.Contains(p.Key)) ||
                detail.OwnVotes.Count != polls.Count || detail.OwnVotes.Any(p => !polls.ContainsKey(p.Key) || p.Value is { } vote && (vote.PollId != p.Key || !polls[p.Key].Options.Any(o => o.OptionId == vote.OptionId))) ||
                detail.Results.Any(p => p.Value is null || p.Value.PollId != p.Key || !polls.ContainsKey(p.Key) || p.Value.Options.Any(o => !polls[p.Key].Options.Any(v => v.OptionId == o.OptionId)))) return false;
        }
        return true;
    }

    private async Task SaveCacheAsync(CachePermit? permit, Access ticket, Func<CommunityReadCache, CommunityReadCache> update)
    {
        if (permit is null) return;
        await cacheGate.WaitAsync();
        try
        {
            if (!Current(ticket) || !CacheScopeMatches(permit)) return;
            var key = CacheKey(permit.Owner, permit.Stamp);
            if ((await storage.ReadAsync<string>("preferences", key + ":fence") ?? "initial") != permit.Fence) return;
            var before = await ReadCacheAsync(permit) ?? new(1, permit.Owner, permit.Stamp, permit.Fence, clock.GetUtcNow(), [], [], []);
            var candidate = update(before) with { SavedAt = clock.GetUtcNow() };
            if (!ValidCache(candidate, permit) || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(candidate, BrowserStorage.Json)) > 2 * 1024 * 1024)
                throw new InvalidDataException("Community cache bounds.");
            // A successful list can revoke previously cached rights without a 403 and
            // without this tab ever selecting that community. Fence every such change
            // against the durable envelope before publishing the replacement snapshot.
            var permissionChanged = before.Communities.Any(previous => previous.Role is not null &&
                candidate.Communities.FirstOrDefault(current => current.CommunityId == previous.CommunityId)?.Role != previous.Role);
            if (permissionChanged || invalidatedCaches.Contains(key))
            {
                invalidatedCaches.Add(key);
                var fence = Guid.NewGuid().ToString("D");
                await storage.WriteAsync("preferences", key + ":fence", fence);
                permit = permit with { Fence = fence };
                candidate = candidate with { Fence = fence };
            }
            if (!Current(ticket) || !CacheScopeMatches(permit)) return;
            await storage.WriteAsync("preferences", key, candidate);
            invalidatedCaches.Remove(key); if (Current(ticket) && CacheScopeMatches(permit)) displayedCache = permit;
        }
        catch (Exception e) when (CacheError(e)) { if (Current(ticket)) Notice = "Новые данные прочитаны, но не сохранены для работы без сети. Предыдущая копия не удалена."; }
        finally { cacheGate.Release(); }
    }

    private Task SaveListAsync(CachePermit? permit, Access ticket)
    {
        var communities = Communities.ToArray(); var requests = new Dictionary<Guid, JoinRequestResponse?>(OwnRequests);
        return SaveCacheAsync(permit, ticket, previous => previous with { Communities = communities, OwnRequests = requests,
            Details = previous.Details.Where(p => communities.Any(c => c.CommunityId == p.Key && c.Role is not null && c.Role == p.Value.Community.Role)).ToDictionary(p => p.Key, p => p.Value) });
    }
    private Task SaveDetailAsync(CachePermit? permit, Access ticket)
    {
        if (Selected is not { Role: not null } selected) return SaveListAsync(permit, ticket);
        var detail = new CommunityDetailCache(selected, clock.GetUtcNow(), Homework.ToArray(), Completions.ToDictionary(p => p.Key, p => p.Value), Announcements.ToArray(), Polls.ToArray(), Members.ToArray(), Staff.ToArray(), JoinRequests.ToArray(), new(OwnVotes), new(Results));
        var own = OwnRequests.GetValueOrDefault(selected.CommunityId);
        return SaveCacheAsync(permit, ticket, previous =>
        {
            var details = new Dictionary<Guid, CommunityDetailCache>(previous.Details) { [selected.CommunityId] = detail };
            details = details.OrderByDescending(p => p.Value.SavedAt).Take(20).ToDictionary(p => p.Key, p => p.Value);
            var requests = new Dictionary<Guid, JoinRequestResponse?>(previous.OwnRequests) { [selected.CommunityId] = own };
            return previous with { Communities = previous.Communities.Where(c => c.CommunityId != selected.CommunityId).Append(selected).ToArray(), OwnRequests = requests, Details = details };
        });
    }

    private async Task<bool> RestoreCacheAsync(Guid? communityId = null)
    {
        if (api.Transitioning || state.IsGuest || api.ObservedSessionGeneration is not { Length: > 0 and <= 128 } stamp) return false;
        var ownerKey = state.ProfileKey; var profileGeneration = state.Generation;
        await cacheGate.WaitAsync();
        try
        {
            var identity = await storage.ReadAsync<CachedIdentity>("preferences", "activeIdentity");
            if (identity?.Owner != ownerKey || identity.SessionGeneration != stamp || await storage.ReadAsync<bool>("preferences", "pendingLogout")) return false;
            var fence = await storage.ReadAsync<string>("preferences", CacheKey(ownerKey, stamp) + ":fence") ?? "initial";
            var permit = new CachePermit(ownerKey, stamp, fence); var cached = await ReadCacheAsync(permit);
            if (cached is null || !CacheScopeMatches(permit) || state.Generation != profileGeneration) return false;
            Communities = cached.Communities; OwnRequests.Clear(); foreach (var pair in cached.OwnRequests) OwnRequests[pair.Key] = pair.Value;
            ClearDetail(); Selected = null; IsCached = true; displayedCache = permit; CachedAt = cached.SavedAt; RequiresRefresh = true;
            Notice = "Нет связи с сервером. Сохранённая копия — только для чтения. Права и изменения будут проверены после подключения.";
            if (communityId is { } id)
            {
                if (!cached.Details.TryGetValue(id, out var detail)) { Error = "Это сообщество ещё не сохранено для чтения без сети."; return true; }
                Selected = detail.Community; Homework = detail.Homework; Completions = detail.Completions; Announcements = detail.Announcements; Polls = detail.Polls;
                Members = detail.Members; Staff = detail.Staff; JoinRequests = detail.JoinRequests;
                foreach (var pair in detail.OwnVotes) OwnVotes[pair.Key] = pair.Value;
                foreach (var pair in detail.Results) Results[pair.Key] = pair.Value;
                CachedAt = detail.SavedAt;
            }
            return true;
        }
        catch (Exception e) when (CacheError(e)) { Error = "Не удалось прочитать сохранённое сообщество. Подключитесь к серверу."; return false; }
        finally { cacheGate.Release(); Changed?.Invoke(); }
    }

    private async Task InvalidateCacheAsync(CachePermit? permit)
    {
        if (permit is null) return;
        var key = CacheKey(permit.Owner, permit.Stamp); invalidatedCaches.Add(key);
        await cacheGate.WaitAsync();
        try
        {
            // A separate fence makes a late write from another tab unreadable after revocation.
            await storage.WriteAsync("preferences", key + ":fence", Guid.NewGuid().ToString("D"));
            await storage.WriteAsync<CommunityReadCache?>("preferences", key, null);
        }
        catch (Exception e) when (CacheError(e)) { if (CacheScopeMatches(permit)) Notice = "Копия скрыта, но хранилище не подтвердило её удаление. Освободите место и повторите обновление."; }
        finally { cacheGate.Release(); }
    }
    private async Task ReadFailureAsync(BrowserApiException error, Access ticket, CachePermit? permit, Guid? id = null)
    {
        if (!Current(ticket)) return;
        Failure(error);
        if (error.Status is 401 or 403 or 404)
        {
            generation++; readRequest?.Cancel(); Communities = []; OwnRequests.Clear(); ClearDetail(); Selected = null; IsCached = false; RequiresRefresh = true; Busy = Loading = false;
            await InvalidateCacheAsync(permit ?? displayedCache);
        }
        else if (error.Status == 0 || error.Status >= 500)
        {
            if (!await RestoreCacheAsync(id) && Current(ticket)) { IsCached = true; Notice = "Нет связи с сервером. Действия отключены до успешного обновления."; }
        }
        Changed?.Invoke();
    }
    private void MaskCommunity()
    {
        generation++; readRequest?.Cancel(); Communities = []; OwnRequests.Clear(); ClearDetail(); Selected = null;
        IsCached = false; CachedAt = null; Loading = Busy = false; RequiresRefresh = true; Error = Notice = null; Changed?.Invoke();
    }
    private void ProfileChanged()
    {
        if (observedOwner == state.ProfileKey && observedProfileGeneration == state.Generation) return;
        observedOwner = state.ProfileKey; observedProfileGeneration = state.Generation; MaskCommunity();
    }
    private void TransitionChanged() { if (api.Transitioning) MaskCommunity(); }
    private static bool CacheError(Exception error) => error is JSException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException;
}
