using Zapara.Contracts.Sync;
using System.Text.Json;

namespace Zapara.Web.Services;

public sealed partial class WebAppState
{
    private readonly SemaphoreSlim syncGate = new(1, 1);
    public bool Syncing { get; private set; }
    public string? SyncMessage { get; private set; }
    public IReadOnlyList<PendingChange> Conflicts => Profile.Outbox.Where(p => p.Status == "conflict")
        .GroupBy(p => (p.EntityType, p.EntityId)).Select(g => g.OrderByDescending(p => p.ServerRecord?.Revision ?? 0).First()).ToArray();
    public async Task ResolveConflictAsync(Guid operationId, bool keepLocal, long expectedServerRevision, CancellationToken ct = default)
    {
        var owner = ProfileKey;
        var generation = Generation;
        var replacementId = Guid.NewGuid();
        var replacementOperation = Guid.NewGuid();
        var completionOperation = Guid.NewGuid();
        await syncGate.WaitAsync(ct);
        try
        {
            await CommitSyncAsync(owner, () => owner == ProfileKey && generation == Generation, copy =>
            {
                var row = copy.Outbox.FirstOrDefault(p => p.OpId == operationId && p.Status == "conflict")
                    ?? throw new InvalidOperationException("Этот конфликт уже изменился. Откройте его заново.");
                if ((row.ServerRecord?.Revision ?? 0) != expectedServerRevision)
                    throw new InvalidOperationException("На сервере появилась новая версия. Сравните изменения ещё раз.");
                var key = ProfileValues.Key(row.EntityType, row.EntityId);
                copy.Records.TryGetValue(key, out var local);
                var server = row.ServerRecord;
                var related = copy.Outbox.Where(p => p.EntityType == row.EntityType && p.EntityId == row.EntityId)
                    .Select(p => p.RelatedServerRecord).Where(r => r is not null).OrderByDescending(r => r!.Revision).FirstOrDefault();
                var completionKey = ProfileValues.Key("completion", row.EntityId);
                copy.Records.TryGetValue(completionKey, out var oldCompletion);
                if (keepLocal && row.EntityType == "settings" && server?.Tombstone == true)
                    throw new InvalidOperationException("Удалённую серверную запись настроек восстановить нельзя. Примите вариант сервера.");
                copy.Outbox.RemoveAll(p => p.EntityType == row.EntityType && p.EntityId == row.EntityId);
                if (!keepLocal || local is null)
                {
                    if (server is not null) copy.Records[key] = FromRecord(server); else copy.Records.Remove(key);
                    if (row.EntityType == "homework")
                    {
                        if (server is null || server.Tombstone)
                        {
                            copy.Records.Remove(completionKey);
                            copy.Outbox.RemoveAll(p => p.EntityType == "completion" && p.EntityId == row.EntityId);
                        }
                        else if (related is not null) MergeRemote(copy, related);
                    }
                    return;
                }
                if (local.Tombstone)
                {
                    if (server is null || server.Tombstone)
                    { if (server is not null) copy.Records[key] = FromRecord(server); else copy.Records.Remove(key); return; }
                    local.Revision = server.Revision;
                    ApplyToCopy(copy, new ProfileChange(row.EntityType, row.EntityId, null) { OpId = replacementOperation });
                    return;
                }
                var value = DecodeValue(row.EntityType, local.Value) ?? throw new InvalidDataException();
                var restoreCopy = server?.Tombstone == true || server is null && local.Revision > 0 && row.EntityType != "settings";
                if (restoreCopy)
                {
                    if (server is not null) copy.Records[key] = FromRecord(server); else copy.Records.Remove(key);
                    ApplyToCopy(copy, new ProfileChange(row.EntityType, replacementId, value) { OpId = replacementOperation });
                    if (row.EntityType == "homework")
                    {
                        copy.Records.Remove(completionKey);
                        copy.Outbox.RemoveAll(p => p.EntityType == "completion" && p.EntityId == row.EntityId);
                        if (oldCompletion is { Tombstone: false } && DecodeValue("completion", oldCompletion.Value) is { } completion)
                            ApplyToCopy(copy, new ProfileChange("completion", replacementId, completion) { OpId = completionOperation });
                    }
                }
                else
                {
                    local.Revision = server?.Revision ?? 0;
                    value = (value, server?.Value) switch
                    {
                        (HomeworkValue h, HomeworkValue previous) => new HomeworkValue(h.SubjectRaw, h.SubjectKey, h.Text, h.TargetNthOccurrence, previous.CreatedAtUtc, previous.LegacyCreatedLocalDate),
                        (OverrideValue o, OverrideValue previous) => new OverrideValue(o.SubjectRaw, o.SubjectKey, o.Scope, o.DisplayName, o.Note, previous.CreatedAtUtc),
                        _ => value
                    };
                    ApplyToCopy(copy, new ProfileChange(row.EntityType, row.EntityId, value) { OpId = replacementOperation });
                    if (row.EntityType == "homework" && related is not null) MergeRemote(copy, related);
                }
            }, ct);
            SyncMessage = "Выбранная версия сохранена. Изменения будут синхронизированы.";
        }
        finally { syncGate.Release(); Notify(); WakeRuntime(); }
    }

    public async Task SynchronizeAsync(CancellationToken ct = default, bool refreshPublic = true)
    {
        if (api is null || !Authenticated || !await syncGate.WaitAsync(0, ct)) return;
        var owner = ProfileKey;
        var generation = Generation;
        var family = api.Session.FamilyId;
        var originalGroup = GroupId;
        bool Current() => generation == Generation && owner == ProfileKey && family is not null && api.Session.FamilyId == family;
        Syncing = true;
        Notify();
        try
        {
            if (!Current()) return;
            if (!Profile.HasSnapshot || Profile.SyncEpoch is null)
                await FullResyncAsync(owner, family!.Value, Current, ct);
            if (!Current() || Profile.SyncEpoch is null) return;
            for (var sent = 0; sent < 100 && Current(); sent++)
            {
                var queued = Profile.Outbox.FirstOrDefault(p => p.Status == "pending" &&
                    !Profile.Outbox.Any(c => c.Status == "conflict" && c.EntityId == p.EntityId));
                if (queued is null) break;
                SyncMutation? mutation = null;
                await CommitSyncAsync(owner, Current, copy =>
                {
                    var row = copy.Outbox.FirstOrDefault(p => p.OpId == queued.OpId && p.Status == "pending");
                    if (row is null || copy.SyncEpoch is null) return;
                    if (row.Action == "delete" && row.ExpectedRevision == 0) return;
                    row.SyncEpoch ??= copy.SyncEpoch;
                    mutation = new(row.SyncEpoch.Value, row.OpId, row.EntityType, row.EntityId, row.ExpectedRevision, row.Action,
                        DecodeValue(row.EntityType, row.Value));
                }, ct);
                if (mutation is null || !Current()) break;
                SyncMutationResult outcome;
                try { outcome = await api.SendAsync<SyncMutationResult>(HttpMethod.Post, "sync/mutations", mutation, family, ct); }
                catch (BrowserApiException e) when (e.Code == "sync_reset")
                { await FullResyncAsync(owner, family!.Value, Current, ct); break; }
                if (!Current()) return;
                await CommitSyncAsync(owner, Current, copy =>
                {
                    var row = copy.Outbox.FirstOrDefault(p => p.OpId == mutation.OpId);
                    if (row is null || copy.SyncEpoch != mutation.SyncEpoch) return;
                    var observed = copy.Outbox.Where(p => p.EntityType == row.EntityType && p.EntityId == row.EntityId)
                        .Select(p => p.ServerRecord).Where(r => r is not null).OrderByDescending(r => r!.Revision).FirstOrDefault();
                    if (outcome.Status == 200 && outcome.ServerRecord is { } record)
                    {
                        // Another tab may have consumed newer deltas while this acknowledgement
                        // was in flight. Its shared cursor will not replay that remote variant.
                        if (observed is not null && observed.Revision > record.Revision)
                        {
                            foreach (var pending in copy.Outbox.Where(p => p.EntityType == row.EntityType && p.EntityId == row.EntityId))
                                SetConflict(pending, observed, "revision_conflict");
                            return;
                        }
                        var related = row.EntityType == "homework" ? copy.Outbox.Where(p => p.EntityType == "homework" && p.EntityId == row.EntityId)
                            .Select(p => p.RelatedServerRecord).Where(r => r is not null).OrderByDescending(r => r!.Revision).FirstOrDefault() : null;
                        copy.Outbox.Remove(row);
                        var remaining = copy.Outbox.Where(p => p.EntityType == row.EntityType && p.EntityId == row.EntityId).ToArray();
                        foreach (var next in remaining.Where(p => p.SyncEpoch is null && p.Status == "pending")) next.ExpectedRevision = Math.Max(next.ExpectedRevision, record.Revision);
                        var key = ProfileValues.Key(record.EntityType, record.EntityId);
                        copy.Records.TryGetValue(key, out var local);
                        if (remaining.Length == 0 && (local is null || local.Revision <= record.Revision)) copy.Records[key] = FromRecord(record);
                        else if (local is not null) local.Revision = Math.Max(local.Revision, record.Revision);
                        if (related is not null) MergeRemote(copy, related);
                    }
                    else
                    {
                        SetConflict(row, Newest(observed, outcome.ServerRecord), outcome.Code);
                    }
                }, ct);
            }
            if (!Current()) return;
            await PullChangesAsync(owner, family!.Value, Current, ct);
            if (!Current()) return;
            await CommitSyncAsync(owner, Current, copy => copy.LastSyncedAt = DateTimeOffset.UtcNow, ct);
            SyncMessage = Conflicts.Count > 0 ? "Есть изменения, для которых нужно выбрать версию." : Profile.Outbox.Count > 0 ? "Часть изменений ожидает отправки." : "Данные синхронизированы";
            if (refreshPublic && !string.IsNullOrEmpty(GroupId) && (originalGroup != GroupId || !HasGroupData(GroupId))) await RefreshAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (BrowserApiException e) { if (Current()) SyncMessage = e.Message; }
        catch (Exception e) when (e is Microsoft.JSInterop.JSException or JsonException or ArgumentException or InvalidDataException or BrowserStorageConflictException)
        { if (Current()) SyncMessage = "Не удалось принять изменения. Ваши локальные данные сохранены."; }
        finally { Syncing = false; syncGate.Release(); Notify(); }
    }

    private async Task FullResyncAsync(string owner, Guid family, Func<bool> current, CancellationToken ct)
    {
        // Staging stays separate until every manifest page is validated. A
        // failed/expired manifest can be retried without damaging the last copy.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var manifest = await api!.SendAsync<SyncResyncManifest>(HttpMethod.Post, "sync/resync", expectedFamily: family, ct: ct);
            if (!current()) return;
            var records = new Dictionary<string, SyncRecord>(StringComparer.Ordinal);
            long after = 0;
            var expired = false;
            bool more;
            do
            {
                SyncResyncPage page;
                try
                {
                    page = await api.SendAsync<SyncResyncPage>(HttpMethod.Get,
                        $"sync/resync/{manifest.ManifestId:D}?afterOrdinal={after}&limit=200", expectedFamily: family, ct: ct);
                }
                catch (BrowserApiException e) when (e.Code == "manifest_expired") { expired = true; break; }
                if (!current()) return;
                if (page.Manifest != manifest || page.AfterOrdinal != after || (page.HasMore && page.NextAfterOrdinal <= after)) throw new InvalidDataException();
                foreach (var item in page.Items)
                    if (!records.TryAdd(ProfileValues.Key(item.Record.EntityType, item.Record.EntityId), item.Record)) throw new InvalidDataException();
                after = page.NextAfterOrdinal;
                more = page.HasMore;
            } while (more);
            if (expired) continue;
            if (after != manifest.ItemCount) throw new InvalidDataException();
            await CommitSyncAsync(owner, current, copy =>
            {
                if (copy.SyncEpoch == manifest.SyncEpoch && copy.HasSnapshot &&
                    (copy.AfterSequence > manifest.HighWater || copy.Records.Values.Any(r => r.Revision > manifest.HighWater) ||
                     copy.Outbox.Any(row => row.ServerRecord?.Revision > manifest.HighWater || row.RelatedServerRecord?.Revision > manifest.HighWater))) return;
                var epochChanged = copy.SyncEpoch is not null && copy.SyncEpoch != manifest.SyncEpoch;
                var previous = copy.Records;
                copy.Records = records.ToDictionary(p => p.Key, p => FromRecord(p.Value), StringComparer.Ordinal);
                foreach (var row in copy.Outbox)
                {
                    var key = ProfileValues.Key(row.EntityType, row.EntityId);
                    records.TryGetValue(key, out var server);
                    if (previous.TryGetValue(key, out var local)) copy.Records[key] = local;
                    if (row.EntityType == "homework")
                    {
                        var completionKey = ProfileValues.Key("completion", row.EntityId);
                        // Keep authoritative evidence separate from the local completion needed
                        // by restore-as-copy when the parent was deleted or disappeared on reset.
                        // A live parent still takes the snapshot completion unless it has its own edit.
                        records.TryGetValue(completionKey, out var completion);
                        row.RelatedServerRecord = completion;
                        if ((server is null || server.Tombstone) && local is { Tombstone: false } &&
                            previous.TryGetValue(completionKey, out var savedCompletion))
                            copy.Records[completionKey] = savedCompletion;
                    }
                    if (epochChanged || (server?.Revision ?? 0) != row.ExpectedRevision || row.Status == "conflict")
                    {
                        row.Status = "conflict";
                        row.ServerRecord = server;
                        row.ConflictCode = epochChanged ? "sync_reset" : "revision_conflict";
                    }
                }
                copy.SyncEpoch = manifest.SyncEpoch;
                copy.AfterSequence = manifest.HighWater;
                copy.HasSnapshot = true;
            }, ct);
            return;
        }
        throw new BrowserApiException(410, "manifest_expired", "Снимок успел обновиться. Повторите синхронизацию.");
    }

    private async Task PullChangesAsync(string owner, Guid family, Func<bool> current, CancellationToken ct)
    {
        bool more;
        do
        {
            if (!current() || Profile.SyncEpoch is not { } epoch) return;
            var after = Profile.AfterSequence;
            SyncChangesPage page;
            try
            {
                page = await api!.SendAsync<SyncChangesPage>(HttpMethod.Get,
                    $"sync/changes?epoch={epoch:D}&afterSequence={after}&limit=200", expectedFamily: family, ct: ct);
            }
            catch (BrowserApiException e) when (e.Code == "sync_reset") { await FullResyncAsync(owner, family, current, ct); return; }
            if (!current()) return;
            if (page.Metadata.SyncEpoch != epoch || page.AfterSequence != after || (page.HasMore && page.NextAfterSequence <= after)) throw new InvalidDataException();
            await CommitSyncAsync(owner, current, copy =>
            {
                if (copy.SyncEpoch != epoch) throw new InvalidDataException();
                foreach (var change in page.Changes) MergeRemote(copy, change.Record);
                copy.AfterSequence = Math.Max(copy.AfterSequence, page.NextAfterSequence);
            }, ct);
            more = page.HasMore;
        } while (more);
    }

    private async Task CommitSyncAsync(string owner, Func<bool> current, Action<WebProfile> update, CancellationToken ct)
    {
        var resetEpoch = Profile.ResetEpoch;
        await gate.WaitAsync(ct);
        try
        {
            if (!current()) return;
            var copy = await storage.UpdateProfileAsync(owner, value =>
            {
                if (!current()) throw new BrowserApiException(409, "account_changed", "Аккаунт изменился. Повторите синхронизацию.");
                update(value);
                return value;
            }, ct, resetEpoch);
            if (current()) Profile = copy;
        }
        finally { gate.Release(); }
        Notify();
    }

    private static void MergeRemote(WebProfile copy, SyncRecord record)
    {
        var key = ProfileValues.Key(record.EntityType, record.EntityId);
        copy.Records.TryGetValue(key, out var local);
        if (local is not null && local.Revision >= record.Revision) return;
        var pending = copy.Outbox.Where(p => p.EntityType == record.EntityType && p.EntityId == record.EntityId).ToArray();
        var preserveDeletedParentCompletion = false;
        if (record.EntityType == "completion")
        {
            var parents = copy.Outbox.Where(p => p.EntityType == "homework" && p.EntityId == record.EntityId).ToArray();
            foreach (var parent in parents)
                parent.RelatedServerRecord = Newest(parent.RelatedServerRecord, record);
            preserveDeletedParentCompletion = copy.Records.TryGetValue(ProfileValues.Key("homework", record.EntityId), out var homework) && !homework.Tombstone &&
                parents.Any(parent => parent.ServerRecord?.Tombstone == true ||
                    parent.Status == "conflict" && parent.ServerRecord is null && parent.ConflictCode is "revision_conflict" or "sync_reset");
        }
        if (pending.Length > 0)
        {
            foreach (var row in pending) SetConflict(row, record, "revision_conflict");
        }
        else if (!preserveDeletedParentCompletion) copy.Records[key] = FromRecord(record);
    }

    private static SyncRecord? Newest(SyncRecord? first, SyncRecord? second) =>
        first is not null && (second is null || first.Revision >= second.Revision) ? first : second;

    private static void SetConflict(PendingChange row, SyncRecord? server, string code)
    {
        row.Status = "conflict";
        if (row.ServerRecord is null || server is not null && server.Revision >= row.ServerRecord.Revision)
        { row.ServerRecord = server; row.ConflictCode = code; }
    }

    private static LocalEntity FromRecord(SyncRecord record) => new()
    {
        EntityType = record.EntityType, EntityId = record.EntityId, Revision = record.Revision,
        Tombstone = record.Tombstone, Value = record.Value is null ? null : ProfileValues.Serialize(record.Value)
    };

    private static SyncValue? DecodeValue(string type, JsonElement? value) => value is null ? null : type switch
    {
        "homework" => value.Value.Deserialize<HomeworkValue>(SyncJson.CreateOptions()),
        "completion" => value.Value.Deserialize<CompletionValue>(SyncJson.CreateOptions()),
        "override" => value.Value.Deserialize<OverrideValue>(SyncJson.CreateOptions()),
        "friend" => value.Value.Deserialize<FriendValue>(SyncJson.CreateOptions()),
        "settings" => value.Value.Deserialize<SettingsValue>(SyncJson.CreateOptions()),
        _ => throw new InvalidDataException()
    };
}
