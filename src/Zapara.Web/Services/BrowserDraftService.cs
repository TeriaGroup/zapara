using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Zapara.Contracts.Sync;
using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Zapara.Web.Services;

public enum DraftFeature { Homework, Friend, Override }
public sealed record DraftScope(string Owner, long Generation, long ResetEpoch = 0);
public sealed record DraftBaseline(long? Revision, string Fingerprint);
public sealed record DraftLease(DraftScope Scope, string TabId, DraftFeature Feature, string EntityKey, DraftBaseline Baseline, Guid TargetId);
public sealed record OpenedDraft<T>(DraftLease Lease, T Value, bool Restored, bool Stale) where T : EditorDraft;
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HomeworkDraft), "homework")]
[JsonDerivedType(typeof(FriendDraft), "friend")]
[JsonDerivedType(typeof(OverrideDraft), "override")]
public abstract record EditorDraft;
public sealed record HomeworkDraft(string Subject, string Text, int Nth, HomeworkValue? Original) : EditorDraft;
public sealed record FriendDraft(string Query, string GroupId, string GroupName, string Names, int Palette, FriendValue? Original) : EditorDraft;
public sealed record OverrideDraft(string Subject, string Scope, string Name, string Note, OverrideValue? Original) : EditorDraft;
public sealed record StoredEditorDraft(int Version, string Owner, string TabId, DraftFeature Feature, string EntityKey,
    DraftBaseline Baseline, DateTimeOffset UpdatedAt, EditorDraft Value, Guid TargetId, long ResetEpoch = 0);

/// <summary>Only the three noncredential editors can write drafts. Queue ownership never follows a session switch.</summary>
public sealed class BrowserDraftService : IAsyncDisposable
{
    private readonly BrowserStorage storage;
    private readonly IJSRuntime js;
    private readonly WebAppState state;
    private readonly BrowserApiClient api;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly object queueGate = new();
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);
    private Task<IJSObjectReference>? module;
    private Task<string>? tab;
    private long sequence;
    private DraftScope observed;
    private bool wasBlocked;
    public BrowserDraftService(BrowserStorage storage, IJSRuntime js, WebAppState state, BrowserApiClient api)
    {
        this.storage = storage; this.js = js; this.state = state; this.api = api;
        observed = CaptureScope(); wasBlocked = api.Transitioning;
        state.Changed += ObserveScope; api.TransitionChanged += ObserveScope;
        state.BeforeReload += FlushAsync;
    }
    public event Action? Changed;
    public event Action? ScopeChanged;
    public string? Error { get; private set; }
    public bool HasPending { get { lock (queueGate) return pending.Count > 0; } }
    public bool CanEdit => state.StorageAvailable && !api.Transitioning;
    public DraftScope CaptureScope() => new(state.ProfileKey, state.Generation, state.Profile.ResetEpoch);
    public bool IsCurrent(DraftScope scope) => CanEdit && scope.Owner == state.ProfileKey && scope.Generation == state.Generation && scope.ResetEpoch == state.Profile.ResetEpoch;
    public bool IsCurrent(DraftLease? lease) => lease is not null && IsCurrent(lease.Scope);
    public void RequireCurrent(DraftScope scope)
    {
        if (!IsCurrent(scope)) throw new BrowserApiException(409, "account_changed", "Профиль изменился. Откройте редактор заново; черновик сохранён в прежнем профиле.");
    }
    public static DraftBaseline Baseline(long? revision, SyncValue? original) => new(revision, Hash(original is null ? "null" : ProfileValues.Serialize(original).GetRawText()));
    public static bool IsStale(DraftLease lease, long? revision, SyncValue? original) => lease.Baseline != Baseline(revision, original);
    public DraftLease Rebase(DraftLease lease, long? revision, SyncValue? original) { RequireCurrent(lease.Scope); return lease with { Baseline = Baseline(revision, original) }; }
    public static string OverrideKey(string subject, string scope, Guid? id) => id?.ToString("D") ?? "new-" + Hash(SyncValidation.NormalizeSubject(subject) + "\n" + scope);

    public Task<OpenedDraft<HomeworkDraft>> OpenHomeworkAsync(string entity, long? revision, HomeworkValue? original, HomeworkDraft initial, CancellationToken ct = default)
        => OpenAsync(DraftFeature.Homework, entity, revision, original, initial, ct);
    public Task<OpenedDraft<FriendDraft>> OpenFriendAsync(string entity, long? revision, FriendValue? original, FriendDraft initial, CancellationToken ct = default)
        => OpenAsync(DraftFeature.Friend, entity, revision, original, initial, ct);
    public Task<OpenedDraft<OverrideDraft>> OpenOverrideAsync(string entity, long? revision, OverrideValue? original, OverrideDraft initial, CancellationToken ct = default)
        => OpenAsync(DraftFeature.Override, entity, revision, original, initial, ct);

    private async Task<OpenedDraft<T>> OpenAsync<T>(DraftFeature feature, string entity, long? revision, SyncValue? original, T initial, CancellationToken ct) where T : EditorDraft
    {
        var scope = CaptureScope(); RequireCurrent(scope); Validate(feature, initial); ValidateEntity(entity);
        var lease = new DraftLease(scope, await TabAsync().WaitAsync(ct), feature, entity, Baseline(revision, original), Guid.TryParse(entity, out var parsed) ? parsed : Guid.NewGuid());
        RequireCurrent(scope);
        var key = Key(lease);
        StoredEditorDraft? saved;
        Pending? queued;
        lock (queueGate) pending.TryGetValue(key, out queued);
        saved = queued is not null ? queued.Draft : await storage.ReadAsync<StoredEditorDraft>("preferences", key);
        ct.ThrowIfCancellationRequested(); RequireCurrent(scope);
        if (saved is null) return new(lease, initial, false, false);
        if (saved.ResetEpoch != scope.ResetEpoch) throw new BrowserStorageResetException();
        if (saved.Version != 1 || saved.Owner != scope.Owner || saved.TabId != lease.TabId || saved.Feature != feature || saved.EntityKey != entity || saved.TargetId == Guid.Empty || saved.Value is not T payload)
            throw new InvalidDataException("Черновик не соответствует этому редактору. Данные не изменены.");
        Validate(feature, payload);
        if (Baseline(saved.Baseline.Revision, Original(payload)) != saved.Baseline) throw new InvalidDataException("Исходные данные черновика повреждены.");
        return new(lease with { Baseline = saved.Baseline, TargetId = saved.TargetId }, payload, true, saved.Baseline != lease.Baseline);
    }

    public Task UpdateAsync(DraftLease lease, HomeworkDraft value, CancellationToken ct = default) => QueueAsync(lease, value, ct);
    public Task UpdateAsync(DraftLease lease, FriendDraft value, CancellationToken ct = default) => QueueAsync(lease, value, ct);
    public Task UpdateAsync(DraftLease lease, OverrideDraft value, CancellationToken ct = default) => QueueAsync(lease, value, ct);
    private Task QueueAsync(DraftLease lease, EditorDraft value, CancellationToken ct)
    {
        RequireCurrent(lease.Scope); Validate(lease.Feature, value);
        if (Baseline(lease.Baseline.Revision, Original(value)) != lease.Baseline) throw new InvalidDataException("Исходная запись изменилась. Сравните её с черновиком.");
        Enqueue(lease, new(1, lease.Scope.Owner, lease.TabId, lease.Feature, lease.EntityKey, lease.Baseline, DateTimeOffset.UtcNow, value, lease.TargetId, lease.Scope.ResetEpoch));
        return FlushAsync(ct);
    }

    /// <summary>Only explicit cancel or a successful profile commit calls this; switching owners never clears drafts.</summary>
    public Task ClearAsync(DraftLease lease, CancellationToken ct = default)
    {
        Enqueue(lease, null);
        return FlushAsync(ct);
    }
    private void Enqueue(DraftLease lease, StoredEditorDraft? value)
    {
        lock (queueGate) pending[Key(lease)] = new(++sequence, value, lease.Scope.Owner, lease.Scope.ResetEpoch);
        Changed?.Invoke();
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        await writer.WaitAsync(ct);
        try
        {
            if (!HasPending) return;
            await MarkPending(true);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                KeyValuePair<string, Pending> next;
                lock (queueGate) { if (pending.Count == 0) break; next = pending.First(); }
                // Once an IndexedDB write starts it runs to its transaction result, even if a
                // caller leaves the editor. Newer input remains queued behind this exact value.
                try { await storage.WriteProfilePreferenceAsync(next.Value.Owner, next.Value.ResetEpoch, next.Key, next.Value.Draft); }
                catch (BrowserStorageResetException) { /* Explicit profile cleanup invalidated this older editor lease. */ }
                lock (queueGate) { if (pending.TryGetValue(next.Key, out var latest) && latest.Sequence == next.Value.Sequence) pending.Remove(next.Key); }
            }
            Error = null;
        }
        catch (Exception e) when (e is JSException or JsonException or InvalidDataException)
        {
            Error = "Черновик пока не сохранён. Проверьте свободное место; не закрывайте страницу до сохранения.";
            throw;
        }
        finally { writer.Release(); await MarkPending(HasPending); Changed?.Invoke(); }
    }

    public static string Key(DraftLease lease)
    {
        ValidateEntity(lease.EntityKey);
        if (lease.Scope.Owner.Length is 0 or > 2048 || !Guid.TryParse(lease.TabId, out _) || !Enum.IsDefined(lease.Feature)) throw new ArgumentException("Некорректный владелец черновика.");
        return "draft:" + Uri.EscapeDataString(lease.Scope.Owner) + ":" + lease.TabId + ":" + lease.Feature.ToString().ToLowerInvariant() + ":" + lease.EntityKey;
    }
    private Task<IJSObjectReference> Module => module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/drafts.js").AsTask();
    private Task<string> TabAsync() => tab ??= ReadTabAsync();
    private async Task<string> ReadTabAsync()
    {
        var id = await (await Module).InvokeAsync<string>("tabId");
        return Guid.TryParse(id, out var value) ? value.ToString("D") : throw new JSException("Не удалось определить эту вкладку для сохранения черновиков.");
    }
    private async Task MarkPending(bool value)
    {
        if (module is null) return;
        try { await (await Module).InvokeVoidAsync("setPending", value); } catch (JSException) { }
    }
    private void ObserveScope()
    {
        var current = CaptureScope(); var blocked = api.Transitioning;
        if (observed == current && wasBlocked == blocked) return;
        observed = current; wasBlocked = blocked;
        ScopeChanged?.Invoke(); Changed?.Invoke();
    }
    private static void ValidateEntity(string value) { if (value.Length is 0 or > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new ArgumentException("Некорректный идентификатор черновика."); }
    private static void Validate(DraftFeature feature, EditorDraft value)
    {
        static void Text(string? text, int limit) { if (text is null || text.Length > limit * 2) throw new ArgumentException("Черновик слишком большой."); }
        switch (feature, value)
        {
            case (DraftFeature.Homework, HomeworkDraft h): Text(h.Subject, 256); Text(h.Text, 4000); if (h.Nth is < 1 or > 10) throw new ArgumentException(); break;
            case (DraftFeature.Friend, FriendDraft f): Text(f.Query, 256); Text(f.GroupId, 64); Text(f.GroupName, 256); Text(f.Names, 4000); if (f.Palette is < 1 or > 5) throw new ArgumentException(); break;
            case (DraftFeature.Override, OverrideDraft o):
                Text(o.Subject, 256); Text(o.Name, 256); Text(o.Note, 4000);
                if (o.Scope != "global" && !(o.Scope.Length == 9 && o.Scope.StartsWith("weekday:", StringComparison.Ordinal) && o.Scope[8] is >= '1' and <= '7')) throw new ArgumentException();
                break;
            default: throw new ArgumentException("Этот тип формы не сохраняется в черновиках.");
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static SyncValue? Original(EditorDraft draft) => draft switch { HomeworkDraft h => h.Original, FriendDraft f => f.Original, OverrideDraft o => o.Original, _ => null };
    private sealed record Pending(long Sequence, StoredEditorDraft? Draft, string Owner, long ResetEpoch);
    public async ValueTask DisposeAsync()
    {
        state.Changed -= ObserveScope; api.TransitionChanged -= ObserveScope; state.BeforeReload -= FlushAsync;
        if (module?.IsCompletedSuccessfully == true) { await module.Result.InvokeVoidAsync("dispose"); await module.Result.DisposeAsync(); }
    }
}

public static class EditorSubjectTitles
{
    private static readonly HashSet<string> KnownTypes = new(["лек", "пр", "лаб", "конс", "зач", "экз", "курс", "практика"], StringComparer.OrdinalIgnoreCase);
    public static string TypeOf(string raw, IEnumerable<Lesson> lessons, string groupId)
    {
        var matched = lessons.FirstOrDefault(l => l.GroupId == groupId && ParityService.SameSubject(l.SubjectRaw, raw));
        if (!string.IsNullOrWhiteSpace(matched?.TypeRaw)) return matched.TypeRaw;
        var token = raw.Split(' ', 2)[0];
        return KnownTypes.Contains(token) ? token : "";
    }
}
