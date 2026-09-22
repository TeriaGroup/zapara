using System.Net.Http.Json;
using System.Text.Json;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Timetable;
using Zapara.Client.Domain;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public sealed partial class WebAppState(HttpClient http, BrowserStorage storage, BrowserApiClient? api = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Task? initialization;
    private bool refreshQueued;
    private Guid? appliedFamily;
    private string? appliedSessionGeneration;
    public event Action? Changed;
    public event Func<CancellationToken, Task>? BeforeReload;
    public WebProfile Profile { get; private set; } = new();
    public DevicePreferences Preferences { get; private set; } = new();
    public ScheduleSnapshot? Schedule { get; private set; }
    public string PeriodTitle { get; private set; } = "";
    public bool Ready { get; private set; }
    public bool Refreshing { get; private set; }
    public bool FromBundle { get; private set; }
    public bool StorageAvailable { get; private set; } = true;
    private IReadOnlyList<string> loadedGroupIds = [];
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public long Generation { get; private set; }
    public bool IsGuest => Profile.Owner == "guest";
    public string ProfileKey => Profile.Owner;
    public string AccountName => IsGuest ? "Гость" : Preferences.AccountName ?? "Мой аккаунт";
    public bool Authenticated => api?.Session.Authenticated == true && Profile.Owner == OwnerKey(api.Session);
    public SettingsValue Settings => Values<SettingsValue>("settings").FirstOrDefault()?.Value ?? ProfileValues.DefaultSettings;
    public string GroupId => ResolveGroupId(Settings.SelectedGroupId);
    public string ResolveGroupId(string? id, string? name = null) => Groups.FirstOrDefault(g => g.Id == id)?.Id
        ?? Groups.FirstOrDefault(g => g.Name.Equals(name ?? id, StringComparison.OrdinalIgnoreCase))?.Id ?? id ?? "";
    public bool HasGroupData(string? id) => !string.IsNullOrEmpty(id) && (FromBundle && Groups.Any(g => g.Id == ResolveGroupId(id)) || loadedGroupIds.Contains(ResolveGroupId(id)));
    public string GroupName => Schedule?.Groups.FirstOrDefault(g => g.Id == GroupId)?.Name ?? "Выбрать группу";
    public IReadOnlyList<Group> Groups => Schedule?.Groups ?? [];
    public DateTime Today => DateTime.Today;

    public Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        await storage.DropHeavyLocalCopiesAsync();
        try
        {
            Preferences = await storage.ReadAsync<DevicePreferences>("preferences", "device") ?? new();
            if (await storage.ReadAsync<CachedIdentity>("preferences", "activeIdentity") is { } identity &&
                (identity.Owner == "guest" || api is not null && !api.Transitioning &&
                    identity.SessionGeneration is not null && identity.SessionGeneration == api.ObservedSessionGeneration))
                Preferences = Preferences with { ActiveOwner = identity.Owner, AccountName = identity.Name };
            var owner = Preferences.ActiveOwner;
            if (owner != "guest" && !owner.StartsWith("account@" + http.BaseAddress!.GetLeftPart(UriPartial.Authority) + "#", StringComparison.Ordinal)) owner = "guest";
            Profile = await storage.ReadAsync<WebProfile>("profiles", owner) ?? new() { Owner = owner };
            if (Profile.Owner != owner) throw new JsonException("Profile owner mismatch.");
            var cached = await storage.ReadAsync<PublicCache>("public", "schedule:" + owner) ?? await storage.ReadAsync<PublicCache>("public", "schedule");
            if (cached is not null) SetPublicCache(cached);
            await storage.AppearanceAsync(Preferences.Theme, Preferences.Animations);
        }
        catch (Exception e) when (e is Microsoft.JSInterop.JSException or JsonException)
        {
            StorageAvailable = false;
            Error = "Хранилище устройства недоступно. Изменения пока нельзя сохранить.";
        }
        if (api is not null)
        {
            api.SessionChanged += ApplySessionAsync;
            try { await api.RefreshSessionAsync(); }
            catch (Exception e) when (e is BrowserApiException or Microsoft.JSInterop.JSException or JsonException)
            { Notice = "Нет связи с сервером. Доступны сохранённые данные этого профиля."; }
        }
        await RefreshAsync();
        Ready = true;
        Notify();
    }

    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Refreshing) { refreshQueued = true; return; }
        Refreshing = true;
        var generation = Generation;
        Notify();
        try
        {
            var catalog = await http.GetFromJsonAsync<GroupsEnvelope>("/api/v1/groups", ct) ?? throw new InvalidDataException();
            if (catalog.Groups.Count == 0 || catalog.Meta.SnapshotId == Guid.Empty || catalog.Period.WeekCount < 1) throw new InvalidDataException();
            if (generation != Generation) return;
            var originalGroupId = GroupId;
            var selectedName = Schedule?.Groups.FirstOrDefault(g => g.Id == originalGroupId)?.Name;
            string? Resolve(string? id, string? name)
            {
                var byId = catalog.Groups.FirstOrDefault(g => g.Id == id)?.Id;
                if (byId is not null) return byId;
                if (string.IsNullOrWhiteSpace(name)) return null;
                var named = catalog.Groups.Where(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                return named.Length == 1 ? named[0].Id : null;
            }
            var selectedId = Resolve(originalGroupId, selectedName);
            if (selectedId is null && Schedule?.Lessons.Any(lesson => lesson.GroupId == originalGroupId) == true)
            {
                Notice = "Не удалось сопоставить выбранную группу с каталогом. Прежнее расписание и домашние задания сохранены.";
                return;
            }
            var friends = Values<FriendValue>("friend").ToArray();
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(originalGroupId) && selectedId is not null && selectedId != originalGroupId) idMap[originalGroupId] = selectedId;
            foreach (var friend in friends)
                if (Resolve(friend.Value.GroupId, friend.Value.GroupName) is { } mapped && mapped != friend.Value.GroupId && friend.Value.GroupId is not null)
                    idMap[friend.Value.GroupId] = mapped;
            var required = friends.Where(f => f.Value.Enabled).Select(f => Resolve(f.Value.GroupId, f.Value.GroupName))
                .Append(selectedId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            var lessons = new List<Lesson>();
            var fetched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in required)
            {
                if (catalog.Groups.All(g => g.Id != id)) continue;
                var page = await http.GetFromJsonAsync<LessonsEnvelope>("/api/v1/groups/" + Uri.EscapeDataString(id!) + "/timetable?snapshotId=" + catalog.Meta.SnapshotId, ct)
                    ?? throw new InvalidDataException();
                if (page.Meta.SnapshotId != catalog.Meta.SnapshotId || page.Group.Id != id) throw new InvalidDataException();
                fetched.Add(id!);
                foreach (var lesson in page.Lessons) { lesson.GroupId = id!; lessons.Add(lesson); }
            }
            if (Schedule is not null)
                foreach (var lesson in Schedule.Lessons)
                {
                    var groupId = idMap.TryGetValue(lesson.GroupId ?? "", out var mapped) ? mapped : lesson.GroupId ?? "";
                    if (fetched.Contains(groupId)) continue;
                    lessons.Add(CopyLesson(lesson, groupId));
                }
            var snapshot = new ScheduleSnapshot(catalog.Period.Start.ToDateTime(TimeOnly.MinValue), catalog.Period.WeekCount,
                catalog.Groups.Select(g => new Group { Id = g.Id, Name = g.Name }).ToArray(), lessons,
                catalog.Meta.SnapshotId.ToString("D"), catalog.Meta.FetchedAt);
            if (generation != Generation) return;
            if (originalGroupId != GroupId) { refreshQueued = true; return; }
            var changes = new List<CatalogGroupRemap>();
            if (selectedId is not null && selectedId != Settings.SelectedGroupId)
            {
                changes.Add(new("settings", SyncValidation.SettingsId, Settings.SelectedGroupId, null, selectedId));
            }
            foreach (var friend in friends)
                if (Resolve(friend.Value.GroupId, friend.Value.GroupName) is { } id && id != friend.Value.GroupId)
                {
                    var f = friend.Value;
                    changes.Add(new("friend", friend.Id, f.GroupId, f.GroupName, id));
                }
            var loaded = fetched.Concat(lessons.Select(lesson => lesson.GroupId)).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
            await AcceptAsync(snapshot, catalog.Period.Title, false, changes, loaded, ct);
            Notice = catalog.Meta.Stale ? "Показано последнее сохранённое расписание. Источник пока не обновился." : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            if (generation != Generation) return;
            if (Schedule is null)
            {
                try
                {
                    var xml = await http.GetStringAsync("data/TimetableGroup50.xml", ct);
                    var parsed = new TimetableParser().Parse(xml);
                    ct.ThrowIfCancellationRequested();
                    if (generation != Generation) return;
                    var keep = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(GroupId)) keep.Add(GroupId);
                    foreach (var friend in Values<FriendValue>("friend"))
                        if (friend.Value.Enabled && !string.IsNullOrWhiteSpace(friend.Value.GroupId)) keep.Add(friend.Value.GroupId);
                    var lessons = keep.Count == 0 ? Array.Empty<Lesson>() : parsed.lessons.Where(lesson => keep.Contains(lesson.GroupId)).ToArray();
                    await AcceptAsync(new(parsed.periodStart, parsed.weekCount, parsed.groups, lessons), parsed.periodTitle, false, loadedGroups: keep.ToArray(), ct: ct);
                    Notice = "Встроенный снимок расписания. Свежее расписание появится после подключения к серверу.";
                }
                catch (Exception fallback) when (fallback is HttpRequestException or InvalidOperationException or System.Xml.XmlException or ArgumentException or TaskCanceledException)
                { ct.ThrowIfCancellationRequested(); if (generation == Generation) Error = "Не удалось загрузить расписание. Проверьте соединение и повторите попытку."; }
            }
            else Notice = "Расписание не обновилось. Доступна сохранённая копия.";
        }
        finally
        {
            Refreshing = false; Notify();
            if (refreshQueued) { refreshQueued = false; if (!ct.IsCancellationRequested) await RefreshAsync(ct); }
        }
    }

    private static Lesson CopyLesson(Lesson lesson, string groupId) => new()
    {
        Id = lesson.Id, GroupId = groupId, DayOfWeek = lesson.DayOfWeek, Parity = lesson.Parity, Index = lesson.Index,
        TimeStart = lesson.TimeStart, TimeEnd = lesson.TimeEnd, SubjectRaw = lesson.SubjectRaw, SubjectNormalized = lesson.SubjectNormalized,
        TeacherRaw = lesson.TeacherRaw, RoomRaw = lesson.RoomRaw, BuildingRaw = lesson.BuildingRaw, TypeRaw = lesson.TypeRaw, ClassroomRaw = lesson.ClassroomRaw
    };

    private sealed record CatalogGroupRemap(string Type, Guid Id, string? OriginalId, string? OriginalName, string TargetId)
    {
        public Guid OpId { get; } = Guid.NewGuid();
    }

    private async Task AcceptAsync(ScheduleSnapshot value, string title, bool bundle, IReadOnlyList<CatalogGroupRemap>? changes = null, IReadOnlyList<string>? loadedGroups = null, CancellationToken ct = default)
    {
        var owner = ProfileKey;
        var generation = Generation;
        var resetEpoch = Profile.ResetEpoch;
        await gate.WaitAsync(ct);
        try
        {
            if (owner != ProfileKey || generation != Generation) return;
            var copy = CloneProfile();
            for (var attempt = 0; attempt < 8; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (StorageAvailable)
                {
                    var disk = await storage.ReadAsync<WebProfile>("profiles", owner);
                    copy = disk ?? new() { Owner = owner };
                    if (copy.Owner != owner) throw new InvalidDataException("Profile owner mismatch.");
                    BrowserStorage.EnsureResetEpoch(copy, resetEpoch);
                }
                var settings = copy.Records.GetValueOrDefault(ProfileValues.Key("settings", SyncValidation.SettingsId));
                if (!bundle && (settings is null ? null : ProfileValues.Read<SettingsValue>(settings)?.SelectedGroupId) != Settings.SelectedGroupId)
                { Profile = copy; refreshQueued = true; return; }
                var personal = copy.Records.Where(pair => pair.Value.EntityType is "homework" or "completion" or "override").ToArray();
                foreach (var change in changes ?? [])
                {
                    // Public catalog refresh changes group identity only, never revives a
                    // missing/tombstoned entity or replays captured personal fields over a newer tab.
                    if (!copy.Records.TryGetValue(ProfileValues.Key(change.Type, change.Id), out var current) || current.Tombstone) continue;
                    if (change.Type == "friend" && ProfileValues.Read<FriendValue>(current) is { } friend)
                    {
                        if (friend.GroupId != change.OriginalId || friend.GroupName != change.OriginalName) { refreshQueued = true; continue; }
                        ApplyToCopy(copy, new ProfileChange("friend", change.Id, new FriendValue(change.TargetId, friend.GroupName, friend.MemberNames, friend.PaletteIndex, friend.Enabled)) { OpId = change.OpId });
                    }
                    else if (change.Type == "settings" && ProfileValues.Read<SettingsValue>(current) is { } latest)
                    {
                        if (latest.SelectedGroupId != change.OriginalId) { refreshQueued = true; continue; }
                        ApplyToCopy(copy, new ProfileChange("settings", change.Id, new SettingsValue(change.TargetId, latest.ParityInvert, latest.NotifyTime1, latest.NotifyTime2, latest.Strictness, latest.AlwaysShow)) { OpId = change.OpId });
                    }
                }
                foreach (var (key, saved) in personal)
                    if (!copy.Records.ContainsKey(key)) copy.Records[key] = saved;
                if (StorageAvailable)
                {
                    try
                    {
                        if (!await storage.TryCommitSnapshotAsync(owner, copy.StorageRevision, copy, new PublicCache(value, title, bundle, loadedGroups), ct, resetEpoch)) continue;
                        copy.StorageRevision++;
                    }
                    catch (Microsoft.JSInterop.JSException)
                    { Error = "Обновление не сохранено: проверьте свободное место. Предыдущее расписание осталось на устройстве."; return; }
                }
                if (owner != ProfileKey || generation != Generation) return;
                Profile = copy;
                SetPublicCache(new(value, title, bundle, loadedGroups));
                return;
            }
            Error = "Данные меняются в другой вкладке. Повторите обновление расписания.";
        }
        finally { gate.Release(); }
    }

    public IEnumerable<EntityValue<T>> Values<T>(string type)
    {
        foreach (var record in Profile.Records.Values.Where(r => r.EntityType == type && !r.Tombstone))
            if (ProfileValues.Read<T>(record) is { } value) yield return new(record.EntityId, record.Revision, value);
    }

    public Task SaveSettingsAsync(SettingsValue value) => PutAsync("settings", SyncValidation.SettingsId, value);

    public Task PutAsync(string type, Guid id, SyncValue? value) => PutManyAsync(new ProfileChange(type, id, value));

    public Task PutEditorAsync(string owner, long generation, string type, Guid id, SyncValue? value,
        SyncValue? expectedOriginal, long? expectedRevision) => PutEditorManyAsync(owner, generation,
            [new ProfileChange(type, id, value)], type, id, expectedOriginal, expectedRevision);

    public Task PutEditorManyAsync(string owner, long generation, IReadOnlyList<ProfileChange> changes,
        string guardType, Guid guardId, SyncValue? expectedOriginal, long? expectedRevision) =>
        PutCoreAsync(owner, generation, changes, copy =>
        {
            copy.Records.TryGetValue(ProfileValues.Key(guardType, guardId), out var current);
            var matches = expectedRevision is null ? current is null : current is not null &&
                !current.Tombstone && current.Revision == expectedRevision && current.Value is { } actual &&
                expectedOriginal is not null && actual.GetRawText() == ProfileValues.Serialize(expectedOriginal).GetRawText();
            if (!matches) throw new BrowserApiException(409, "editor_changed",
                "Запись изменилась в другой вкладке или на другом устройстве. Черновик сохранён; откройте актуальную запись либо сохраните как новую.");
        });

    public Task PutManyAsync(params ProfileChange[] changes) => PutCoreAsync(ProfileKey, Generation, changes);

    private async Task PutCoreAsync(string owner, long generation, IReadOnlyList<ProfileChange> changes, Action<WebProfile>? validate = null)
    {
        if (!StorageAvailable) throw new InvalidOperationException("Хранилище недоступно. Разрешите сохранение данных в браузере.");
        var resetEpoch = Profile.ResetEpoch;
        await gate.WaitAsync();
        try
        {
            var copy = await storage.UpdateProfileAsync(owner, value =>
            {
                if (owner != ProfileKey || generation != Generation || api?.Transitioning == true)
                    throw new BrowserApiException(409, "account_changed", "Аккаунт изменился. Повторите действие.");
                validate?.Invoke(value);
                foreach (var change in changes) ApplyToCopy(value, change);
                return value;
            }, expectedResetEpoch: resetEpoch);
            if (owner != ProfileKey || generation != Generation) return;
            Profile = copy;
            Error = null;
        }
        finally { gate.Release(); }
        Notify();
        WakeRuntime();
    }

    private WebProfile CloneProfile() => JsonSerializer.Deserialize<WebProfile>(JsonSerializer.Serialize(Profile, BrowserStorage.Json), BrowserStorage.Json)!;

    private static void ApplyToCopy(WebProfile copy, ProfileChange change)
    {
        var (type, id, value) = change;
        var key = ProfileValues.Key(type, id);
        copy.Records.TryGetValue(key, out var existing);
        var revision = existing?.Revision ?? 0;
        var payload = value is null ? (JsonElement?)null : ProfileValues.Serialize(value);
        if (copy.Owner != "guest")
        {
            var unsent = copy.Outbox.LastOrDefault(p => p.EntityType == type && p.EntityId == id && p.SyncEpoch is null && p.Status == "pending");
            if (unsent is not null) copy.Outbox.Remove(unsent);
            if (value is not null || revision > 0 || copy.Outbox.Any(p => p.EntityType == type && p.EntityId == id))
                copy.Outbox.Add(new() { OpId = change.OpId, EntityType = type, EntityId = id, ExpectedRevision = revision, Action = value is null ? "delete" : "upsert", Value = payload });
        }
        copy.Records[key] = new() { EntityType = type, EntityId = id, Revision = revision, Tombstone = value is null, Value = payload };
    }

    public async Task SetAlphaMapsAsync(bool enabled)
    {
        var copy = Preferences with { AlphaMaps = enabled };
        await storage.WriteAsync("preferences", "device", copy);
        Preferences = copy;
        Notify();
    }

    public async Task SetAppearanceAsync(string theme, bool animations)
    {
        if (theme is not ("system" or "light" or "dark")) return;
        var copy = Preferences with { Theme = theme, Animations = animations };
        await storage.WriteAsync("preferences", "device", copy);
        await storage.AppearanceAsync(theme, animations);
        Preferences = copy;
        Notify();
    }

    public string DisplayName(Lesson lesson)
    {
        var values = Values<OverrideValue>("override").Where(x => ParityService.SameSubject(x.Value.SubjectRaw, lesson.SubjectRaw)).Select(x => x.Value).ToArray();
        return values.FirstOrDefault(x => x.Scope == "weekday:" + lesson.DayOfWeek)?.DisplayName
            ?? values.FirstOrDefault(x => x.Scope == "global")?.DisplayName
            ?? SubjectTitle(lesson.SubjectRaw, lesson.TypeRaw);
    }

    public string? LessonNote(Lesson lesson) => Values<OverrideValue>("override")
        .Where(x => ParityService.SameSubject(x.Value.SubjectRaw, lesson.SubjectRaw) && (x.Value.Scope == "global" || x.Value.Scope == "weekday:" + lesson.DayOfWeek))
        .OrderByDescending(x => x.Value.Scope.StartsWith("weekday:", StringComparison.Ordinal)).FirstOrDefault()?.Value.Note;

    public static string SubjectTitle(string subject, string? kind) => !string.IsNullOrWhiteSpace(kind) && subject.StartsWith(kind + " ", StringComparison.OrdinalIgnoreCase)
        ? subject[(kind.Length + 1)..].Trim() : subject;
    public void SetError(string text) { Error = text; Notify(); }
    public void DismissError() { Error = null; Notify(); }
    public void Notify() => Changed?.Invoke();

    public async Task WaitForStorageAsync(CancellationToken ct = default)
    {
        if (BeforeReload is { } flush)
            foreach (Func<CancellationToken, Task> handler in flush.GetInvocationList()) await handler(ct);
        await gate.WaitAsync(ct);
        gate.Release();
    }

    private string OwnerKey(BrowserSession session) => session.Authenticated && session.User is not null
        ? "account@" + http.BaseAddress!.GetLeftPart(UriPartial.Authority) + "#" + session.User.UserId.ToString("D") : "guest";

    private async Task ApplySessionAsync(BrowserSession session)
    {
        var owner = OwnerKey(session);
        var confirmed = api?.ConfirmedSessionGeneration;
        var revision = api?.Revision;
        bool Current() => api is null || api.Revision == revision && api.ConfirmedSessionGeneration == confirmed &&
            api.Session.FamilyId == session.FamilyId && api.Session.User?.UserId == session.User?.UserId;
        await gate.WaitAsync();
        try
        {
            if (!Current()) return;
            if (appliedFamily != session.FamilyId || appliedSessionGeneration != confirmed)
            {
                Generation++;
                appliedFamily = session.FamilyId;
                appliedSessionGeneration = confirmed;
                SyncMessage = null;
            }
            if (Profile.Owner != owner)
            {
                Generation++;
                Profile = new() { Owner = owner };
                SyncMessage = null;
                Notify();
                var loaded = await storage.ReadAsync<WebProfile>("profiles", owner);
                if (!Current()) return;
                if (loaded is not null && loaded.Owner != owner) throw new JsonException("Profile owner mismatch.");
                Profile = loaded ?? new() { Owner = owner };
                var cached = await storage.ReadAsync<PublicCache>("public", "schedule:" + owner);
                if (!Current()) return;
                if (cached is not null) SetPublicCache(cached);
            }
            var preferences = Preferences with { ActiveOwner = owner, AccountName = session.User?.DisplayName ?? session.User?.Username };
            await storage.WriteAsync("preferences", "activeIdentity", new CachedIdentity(owner, preferences.AccountName, confirmed));
            if (!Current()) return;
            Preferences = preferences;
        }
        catch (Exception e) when (e is Microsoft.JSInterop.JSException or JsonException)
        {
            StorageAvailable = false;
            Error = "Не удалось открыть хранилище этого профиля. Локальные изменения временно недоступны.";
        }
        finally { gate.Release(); }
        Notify();
        WakeRuntime();
    }

    private void SetPublicCache(PublicCache cache)
    {
        Schedule = cache.Snapshot;
        PeriodTitle = cache.Title;
        FromBundle = cache.FromBundle;
        loadedGroupIds = cache.LoadedGroupIds ?? cache.Snapshot.Lessons.Select(l => l.GroupId).Distinct().ToArray();
    }
}

public sealed record PublicCache(ScheduleSnapshot Snapshot, string Title, bool FromBundle, IReadOnlyList<string>? LoadedGroupIds = null);
public sealed record WebPeriod(DateOnly Start, int WeekCount, string Title, string TimeZone);
public sealed record WebGroup(string Id, string Name, int LessonCount);
public sealed record WebSnapshot(Guid SnapshotId, DateTimeOffset FetchedAt, DateTimeOffset PublishedAt, bool Stale);
public sealed record GroupsEnvelope(WebPeriod Period, WebSnapshot Meta, List<WebGroup> Groups);
public sealed record LessonsEnvelope(WebPeriod Period, WebSnapshot Meta, WebGroup Group, List<Lesson> Lessons);
