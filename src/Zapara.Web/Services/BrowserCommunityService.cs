using Zapara.Contracts.Communities;

namespace Zapara.Web.Services;

/// <summary>Live authorized operations and a separately guarded, read-only offline cache.</summary>
public sealed partial class BrowserCommunityService : IDisposable
{
    private readonly BrowserApiClient api;
    private readonly TimeProvider clock;
    private CancellationTokenSource? readRequest;
    private Guid? owner, family;
    private long generation;
    private readonly HashSet<Guid> voteLocked = [];
    public BrowserCommunityService(BrowserApiClient api, BrowserStorage storage, WebAppState state, TimeProvider? clock = null)
    {
        this.api = api;
        this.storage = storage; this.state = state;
        this.clock = clock ?? TimeProvider.System;
        owner = api.Session.User?.UserId;
        family = api.Session.FamilyId;
        api.SessionChanged += SessionChanged;
        api.TransitionChanged += TransitionChanged; state.Changed += ProfileChanged;
        observedOwner = state.ProfileKey; observedProfileGeneration = state.Generation;
    }
    public event Action? Changed;
    public IReadOnlyList<CommunityResponse> Communities { get; private set; } = [];
    public CommunityResponse? Selected { get; private set; }
    public IReadOnlyList<HomeworkResponse> Homework { get; private set; } = [];
    public IReadOnlyDictionary<Guid, CompletionResponse> Completions { get; private set; } = new Dictionary<Guid, CompletionResponse>();
    public IReadOnlyList<AnnouncementResponse> Announcements { get; private set; } = [];
    public IReadOnlyList<PollResponse> Polls { get; private set; } = [];
    public IReadOnlyList<MemberResponse> Members { get; private set; } = [];
    public IReadOnlyList<MemberResponse> Staff { get; private set; } = [];
    public IReadOnlyList<JoinRequestResponse> JoinRequests { get; private set; } = [];
    public Dictionary<Guid, JoinRequestResponse?> OwnRequests { get; } = [];
    public Dictionary<Guid, VoteResponse?> OwnVotes { get; } = [];
    public Dictionary<Guid, PollResultsResponse> Results { get; } = [];
    public bool Loading { get; private set; }
    public bool Busy { get; private set; }
    public bool RequiresRefresh { get; private set; }
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public bool IsMember => Selected?.Role is "member" or "headman" or "curator";
    public bool IsStaff => Selected?.Role is "headman" or "curator";
    public bool CanWrite => IsMember && CanJoin;
    public bool CanPublish => CanWrite && IsStaff;
    public bool CanVote(PollResponse poll) => CanWrite && poll.DeadlineAt > clock.GetUtcNow() &&
        !voteLocked.Contains(poll.PollId) && OwnVotes.TryGetValue(poll.PollId, out var vote) && vote is null;

    private async Task SessionChanged(BrowserSession session)
    {
        if (owner != session.User?.UserId || family != session.FamilyId)
        {
            var oldCache = displayedCache;
            owner = session.User?.UserId; family = session.FamilyId;
            MaskCommunity(); displayedCache = null;
            await InvalidateCacheAsync(oldCache);
        }
    }

    public async Task LoadAsync(string? groupId = null)
    {
        if (Busy) return;
        var ticket = Capture();
        if (ticket is null) { await RestoreCacheAsync(); return; }
        var permit = await BeginCacheReadAsync();
        using var request = BeginRead();
        Loading = true; Error = null; Notice = null; Changed?.Invoke();
        try
        {
            var mine = await api.GetAsync<CommunityResponse[]>("communities", request.Token);
            var found = string.IsNullOrWhiteSpace(groupId) ? [] : await api.GetAsync<CommunityResponse[]>("communities?groupId=" + Uri.EscapeDataString(groupId), request.Token);
            var merged = mine.Concat(found).GroupBy(c => c.CommunityId).Select(g => g.FirstOrDefault(c => c.Role is not null) ?? g.First()).OrderBy(c => c.Name).ToArray();
            var pending = new Dictionary<Guid, JoinRequestResponse?>();
            foreach (var community in merged.Where(c => c.Role is null))
                pending[community.CommunityId] = (await api.GetAsync<OwnJoinRequestResponse>(Path(community.CommunityId) + "/join-request", request.Token)).Request;
            if (!Current(ticket.Value) || request.IsCancellationRequested) return;
            Communities = merged;
            if (Selected is { } old && !merged.Any(c => c.CommunityId == old.CommunityId && c.Role == old.Role)) { ClearDetail(); Selected = null; }
            OwnRequests.Clear(); foreach (var pair in pending) OwnRequests[pair.Key] = pair.Value;
            Notice = null; RequiresRefresh = false; IsCached = false; CachedAt = null;
            await SaveListAsync(permit, ticket.Value);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (BrowserApiException e) { if (!request.IsCancellationRequested) await ReadFailureAsync(e, ticket.Value, permit); }
        finally { if (readRequest == request) { readRequest = null; if (Current(ticket.Value)) { Loading = false; Changed?.Invoke(); } } }
    }

    public async Task OpenAsync(Guid id)
    {
        if (Busy) return;
        var ticket = Capture();
        if (ticket is null) { await RestoreCacheAsync(id); return; }
        var permit = await BeginCacheReadAsync();
        using var request = BeginRead();
        if (Selected?.CommunityId != id) { ClearDetail(); Selected = Communities.FirstOrDefault(c => c.CommunityId == id); }
        Loading = true; Error = null; Changed?.Invoke();
        try
        {
            var community = await api.GetAsync<CommunityResponse>(Path(id), request.Token);
            var own = await api.GetAsync<OwnJoinRequestResponse>(Path(id) + "/join-request", request.Token);
            if (!Current(ticket.Value) || request.IsCancellationRequested) return;
            if (Selected is { Role: not null } prior && prior.Role != community.Role)
            {
                ClearDetail(); await InvalidateCacheAsync(permit ?? displayedCache);
                permit = await BeginCacheReadAsync();
                if (!Current(ticket.Value) || request.IsCancellationRequested) return;
            }
            Selected = community; OwnRequests[id] = own.Request;
            Communities = Communities.Where(c => c.CommunityId != id).Append(community).ToArray();
            if (community.Role is null) { ClearDetail(); RequiresRefresh = false; IsCached = false; await SaveListAsync(permit, ticket.Value); return; }
            var homeworkTask = api.GetAsync<HomeworkResponse[]>(Path(id) + "/homework", request.Token);
            var announcementsTask = api.GetAsync<AnnouncementResponse[]>(Path(id) + "/announcements", request.Token);
            var pollsTask = api.GetAsync<PollResponse[]>(Path(id) + "/polls", request.Token);
            var membersTask = api.GetAsync<MemberResponse[]>(Path(id) + "/members", request.Token);
            var staffTask = api.GetAsync<MemberResponse[]>(Path(id) + "/staff", request.Token);
            await Task.WhenAll(homeworkTask, announcementsTask, pollsTask, membersTask, staffTask);
            var completions = new Dictionary<Guid, CompletionResponse>();
            foreach (var item in homeworkTask.Result)
                completions[item.HomeworkId] = await api.GetAsync<CompletionResponse>(Path(id) + $"/homework/{item.HomeworkId:D}/completion", request.Token);
            var votes = new Dictionary<Guid, VoteResponse?>();
            foreach (var item in pollsTask.Result)
                votes[item.PollId] = (await api.GetAsync<OwnVoteResponse>(Path(id) + $"/polls/{item.PollId:D}/vote", request.Token)).Vote;
            var joins = community.Role is "headman" or "curator" ? await api.GetAsync<JoinRequestResponse[]>(Path(id) + "/join-requests", request.Token) : [];
            if (!Current(ticket.Value) || request.IsCancellationRequested) return;
            Homework = homeworkTask.Result; Announcements = announcementsTask.Result; Polls = pollsTask.Result;
            Members = membersTask.Result; Staff = staffTask.Result; JoinRequests = joins; Completions = completions;
            OwnVotes.Clear(); foreach (var pair in votes) OwnVotes[pair.Key] = pair.Value;
            voteLocked.Clear(); Results.Clear(); RequiresRefresh = false; Notice = null; IsCached = false; CachedAt = null;
            Communities = Communities.Select(c => c.CommunityId == id ? community : c).ToArray();
            await SaveDetailAsync(permit, ticket.Value);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (BrowserApiException e) { if (!request.IsCancellationRequested) await ReadFailureAsync(e, ticket.Value, permit, id); }
        finally { if (readRequest == request) { readRequest = null; if (Current(ticket.Value)) { Loading = false; Changed?.Invoke(); } } }
    }

    public void Close() { readRequest?.Cancel(); ClearDetail(); Selected = null; Loading = false; Changed?.Invoke(); }

    public Task<bool> JoinAsync(Guid id, Guid expectedFamily) => Mutate(id, expectedFamily, false, false, async ticket =>
    {
        var result = await api.SendAsync<JoinRequestResponse>(HttpMethod.Post, Path(id) + "/join-requests", expectedFamily: expectedFamily);
        if (Current(ticket)) { OwnRequests[id] = result; Notice = "Заявка отправлена. Решение появится после проверки организаторами сообщества."; }
    });

    public Task<bool> ResolveJoinAsync(Guid id, Guid requestId, bool accept, Guid expectedFamily) => Mutate(id, expectedFamily, true, true, async ticket =>
    {
        var result = await api.SendAsync<JoinRequestResponse>(HttpMethod.Post, Path(id) + $"/join-requests/{requestId:D}/" + (accept ? "accept" : "reject"), expectedFamily: expectedFamily);
        if (Current(ticket))
        {
            JoinRequests = JoinRequests.Where(r => r.RequestId != result.RequestId).ToArray();
            Notice = accept ? "Заявка одобрена." : "Заявка отклонена.";
            if (accept)
            {
                var members = api.GetAsync<MemberResponse[]>(Path(id) + "/members");
                var staffMembers = api.GetAsync<MemberResponse[]>(Path(id) + "/staff");
                await Task.WhenAll(members, staffMembers);
                if (Current(ticket) && Selected?.CommunityId == id) { Members = members.Result; Staff = staffMembers.Result; }
            }
        }
    });

    public Task<bool> SaveHomeworkAsync(Guid id, Guid expectedFamily, string title, string body, HomeworkResponse? existing = null) => Mutate(id, expectedFamily, true, true, async ticket =>
    {
        var request = new HomeworkUpsert(title.Trim(), body, existing?.Revision ?? 0);
        var result = await api.SendAsync<HomeworkResponse>(existing is null ? HttpMethod.Post : HttpMethod.Put,
            Path(id) + "/homework" + (existing is null ? "" : $"/{existing.HomeworkId:D}"), request, expectedFamily);
        if (Current(ticket))
        {
            Homework = Homework.Where(h => h.HomeworkId != result.HomeworkId).Append(result).ToArray();
            var completions = Completions.ToDictionary(p => p.Key, p => p.Value);
            completions.TryAdd(result.HomeworkId, new(result.HomeworkId, false, 0, null)); Completions = completions;
        }
    });

    public Task<bool> SaveAnnouncementAsync(Guid id, Guid expectedFamily, string title, string body, AnnouncementResponse? existing = null) => Mutate(id, expectedFamily, true, true, async ticket =>
    {
        var request = new AnnouncementUpsert(title.Trim(), body, existing?.Revision ?? 0);
        var result = await api.SendAsync<AnnouncementResponse>(existing is null ? HttpMethod.Post : HttpMethod.Put,
            Path(id) + "/announcements" + (existing is null ? "" : $"/{existing.AnnouncementId:D}"), request, expectedFamily);
        if (Current(ticket)) Announcements = Announcements.Where(a => a.AnnouncementId != result.AnnouncementId).Append(result).ToArray();
    });

    public Task<bool> CompleteAsync(Guid id, Guid expectedFamily, HomeworkResponse homework, bool completed) => Mutate(id, expectedFamily, true, false, async ticket =>
    {
        if (!Completions.TryGetValue(homework.HomeworkId, out var before)) throw new BrowserApiException(409, "refresh_required", "Сначала обновите состояние задания.");
        var result = await api.SendAsync<CompletionResponse>(HttpMethod.Put, Path(id) + $"/homework/{homework.HomeworkId:D}/completion", new CompletionUpsert(completed, before.Revision), expectedFamily);
        if (Current(ticket)) Completions = Completions.Where(p => p.Key != result.HomeworkId).Append(new(result.HomeworkId, result)).ToDictionary(p => p.Key, p => p.Value);
    });

    public Task<bool> PublishPollAsync(Guid id, Guid expectedFamily, string question, DateTimeOffset deadline, IReadOnlyList<string> options) => Mutate(id, expectedFamily, true, true, async ticket =>
    {
        if (deadline <= clock.GetUtcNow()) throw new ArgumentException();
        var result = await api.SendAsync<PollResponse>(HttpMethod.Post, Path(id) + "/polls", new PollUpsert(question.Trim(), deadline.ToUniversalTime(), options.Select(o => o.Trim()).ToArray(), 0), expectedFamily);
        if (Current(ticket)) { Polls = Polls.Append(result).ToArray(); OwnVotes[result.PollId] = null; }
    });

    public Task<bool> VoteAsync(Guid id, Guid expectedFamily, PollResponse poll, Guid option) => Mutate(id, expectedFamily, true, false, async ticket =>
    {
        if (poll.DeadlineAt <= clock.GetUtcNow() || voteLocked.Contains(poll.PollId) || !OwnVotes.TryGetValue(poll.PollId, out var own) || own is not null)
            throw new BrowserApiException(409, "poll_closed", "Голосование завершено или ваш голос уже принят. Обновите результаты.");
        try
        {
            var result = await api.SendAsync<VoteResponse>(HttpMethod.Post, Path(id) + $"/polls/{poll.PollId:D}/votes", new VoteRequest(option), expectedFamily);
            if (Current(ticket)) { OwnVotes[poll.PollId] = result; voteLocked.Add(poll.PollId); }
        }
        catch (BrowserApiException e)
        {
            if (Current(ticket) && (e.Status == 0 || e.Code is "already_voted" or "poll_closed")) voteLocked.Add(poll.PollId);
            throw;
        }
    });

    public async Task LoadResultsAsync(Guid id, Guid pollId)
    {
        if (IsCached) { if (!Results.ContainsKey(pollId)) Notice = "Результаты этого опроса ещё не сохранены. Подключитесь к серверу."; Changed?.Invoke(); return; }
        var ticket = Capture();
        if (ticket is null || Selected?.CommunityId != id || !IsMember) return;
        var permit = await BeginCacheReadAsync();
        try
        {
            var result = await api.GetAsync<PollResultsResponse>(Path(id) + $"/polls/{pollId:D}/results");
            if (Current(ticket.Value) && Selected?.CommunityId == id)
            {
                Results[pollId] = result;
                await SaveCacheAsync(permit, ticket.Value, previous =>
                {
                    if (!previous.Details.TryGetValue(id, out var detail)) return previous;
                    var results = new Dictionary<Guid, PollResultsResponse>(detail.Results) { [pollId] = result };
                    var details = new Dictionary<Guid, CommunityDetailCache>(previous.Details) { [id] = detail with { Results = results } };
                    return previous with { Details = details };
                });
            }
        }
        catch (BrowserApiException e) { if (Selected?.CommunityId == id) await ReadFailureAsync(e, ticket.Value, permit, id); }
        finally { Changed?.Invoke(); }
    }

    private async Task<bool> Mutate(Guid id, Guid expectedFamily, bool member, bool staff, Func<Access, Task> action)
    {
        var ticket = Capture();
        if (ticket is null || ticket.Value.Family != expectedFamily) { Error = "Аккаунт изменился. Откройте сообщество заново."; Changed?.Invoke(); return false; }
        if (!CanJoin || (member && (Selected?.CommunityId != id || !IsMember)) || (staff && !IsStaff))
        { Error = "Действие сейчас недоступно. Обновите сообщество и проверьте права."; Changed?.Invoke(); return false; }
        Busy = true; Error = null; Notice = null; Changed?.Invoke();
        try { await action(ticket.Value); return Current(ticket.Value); }
        catch (BrowserApiException e) { await ReadFailureAsync(e, ticket.Value, displayedCache, id); return false; }
        catch (ArgumentException) { if (Current(ticket.Value)) Error = "Проверьте заполнение полей, длину текста и срок голосования."; return false; }
        finally { if (Current(ticket.Value)) { Busy = false; Changed?.Invoke(); } }
    }

    private void Failure(BrowserApiException error)
    {
        Error = error.Message;
        if (error.Status is 401 or 403)
        {
            if (Selected is { } c) { Selected = new(c.CommunityId, c.Name, c.Description, c.Revision, null); Communities = Communities.Select(x => x.CommunityId == c.CommunityId ? Selected : x).ToArray(); }
            ClearDetail(); RequiresRefresh = true;
            Error = error.Status == 403 ? "Права доступа изменились. Обновите сообщество; действия отключены." : "Сессия завершилась. Войдите в аккаунт снова.";
        }
        else if (error.Status == 409)
        {
            RequiresRefresh = true;
            Error = error.Code switch
            {
                "already_voted" => "Ваш голос уже принят. Обновите сообщество, чтобы увидеть свой выбор и результаты.",
                "poll_closed" => "Голосование завершено. Обновите сообщество и посмотрите результаты.",
                "already_requested" => "Заявка уже отправлена. Обновите сообщество, чтобы увидеть её статус.",
                "already_member" => "Вы уже участник. Обновите сообщество, чтобы открыть материалы.",
                _ => "Данные изменились. Обновите сообщество и проверьте результат перед повторной отправкой."
            };
        }
        else if (error.Status == 0 || error.Status >= 500) { RequiresRefresh = true; Error = "Не удалось подтвердить результат. Изменения не поставлены в очередь. Обновите сообщество и проверьте данные перед повторной отправкой."; }
    }
    private Access? Capture() => !api.Transitioning && api.Session is { Authenticated: true, User: { } user, FamilyId: { } id } ? new(user.UserId, id, generation) : null;
    private bool Current(Access ticket) => !api.Transitioning && ticket.Generation == generation && api.Session.Authenticated && api.Session.FamilyId == ticket.Family && api.Session.User?.UserId == ticket.User;
    private CancellationTokenSource BeginRead() { readRequest?.Cancel(); return readRequest = new(); }
    private void ClearDetail() { Homework = []; Completions = new Dictionary<Guid, CompletionResponse>(); Announcements = []; Polls = []; Members = []; Staff = []; JoinRequests = []; OwnVotes.Clear(); Results.Clear(); voteLocked.Clear(); }
    private static string Path(Guid id) => "communities/" + id.ToString("D");
    private readonly record struct Access(Guid User, Guid Family, long Generation);
    public void Dispose() { generation++; api.SessionChanged -= SessionChanged; api.TransitionChanged -= TransitionChanged; state.Changed -= ProfileChanged; readRequest?.Cancel(); readRequest?.Dispose(); }
}
