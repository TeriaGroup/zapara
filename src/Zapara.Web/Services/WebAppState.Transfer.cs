using System.Text;
using System.Text.Json;

namespace Zapara.Web.Services;

public sealed partial class WebAppState
{
    public async Task<TransferPreview> PreviewTransferAsync(TransferSource source, string owner, long generation, Guid? family, CancellationToken ct = default)
    {
        EnsureTransferScope(owner, generation, family, source.Kind == "guest");
        var resetEpoch = Profile.ResetEpoch;
        if (source.Kind == "guest" && !Profile.HasSnapshot) throw new InvalidOperationException("Для переноса гостя сначала получите снимок данных аккаунта.");
        var target = await storage.ReadAsync<WebProfile>("profiles", owner) ?? new() { Owner = owner };
        ct.ThrowIfCancellationRequested();
        EnsureTransferScope(owner, generation, family, source.Kind == "guest");
        if (target.Owner != owner) throw new InvalidDataException("Профиль принадлежит другому аккаунту.");
        BrowserStorage.EnsureResetEpoch(target, resetEpoch);
        return TransferPlanner.Create(source, target, generation, family);
    }

    public async Task<TransferApplyResult> ApplyTransferAsync(TransferPreview preview, CancellationToken ct = default)
    {
        if (!StorageAvailable) throw new InvalidOperationException("Хранилище недоступно. Перенос не выполнен.");
        EnsureTransferScope(preview.Owner, preview.Generation, preview.FamilyId, preview.Source.Kind == "guest");
        var committed = false;
        await gate.WaitAsync(ct);
        try
        {
            var latest = await storage.ReadAsync<WebProfile>("profiles", preview.Owner) ?? new() { Owner = preview.Owner };
            BrowserStorage.EnsureResetEpoch(latest, preview.ResetEpoch);
            EnsureTransferScope(preview.Owner, preview.Generation, preview.FamilyId, preview.Source.Kind == "guest");
            var prepared = TransferPlanner.Prepare(preview, latest);
            if (prepared.Count == 0) return new(0, null, true);
            var now = DateTimeOffset.UtcNow;
            var backupId = Guid.NewGuid();
            var backupKey = "backup:" + preview.Owner + ":" + backupId.ToString("D");
            var payload = JsonSerializer.Serialize(new TransferBackupData(latest.Owner, latest.Records, latest.ImportReceipts), BrowserStorage.Json);
            var backup = new TransferBackup(backupId, preview.Owner, now, LegacyTransferCodec.Digest(Encoding.UTF8.GetBytes(payload)), payload);
            // The independently stored copy must be read back and verified before a
            // mutation of the destination profile is even attempted.
            await storage.WriteAsync("profiles", backupKey, backup);
            var verified = await storage.ReadAsync<TransferBackup>("profiles", backupKey);
            ValidateTransferBackup(verified, preview.Owner, backupId);
            EnsureTransferScope(preview.Owner, preview.Generation, preview.FamilyId, preview.Source.Kind == "guest");
            var receiptKey = "backup:" + backupId.ToString("D");
            var copy = await storage.UpdateProfileAsync(preview.Owner, value =>
            {
                EnsureTransferScope(preview.Owner, preview.Generation, preview.FamilyId, preview.Source.Kind == "guest");
                if (preview.Source.Kind == "guest" && !value.HasSnapshot) throw new InvalidOperationException("Снимок данных аккаунта изменился. Повторите синхронизацию и просмотр переноса.");
                TransferPlanner.Revalidate(preview, value, prepared);
                var changed = 0;
                foreach (var row in prepared)
                {
                    if (value.ImportReceipts.ContainsKey(row.Preview.Record.SourceKey)) continue;
                    if (row.Change is { } change) { ApplyToCopy(value, change); changed++; }
                    value.ImportReceipts.Add(row.Preview.Record.SourceKey, row.Receipt);
                }
                value.ImportReceipts[receiptKey] = new("backup", backupId, BackupKey: backupKey, AppliedAt: now, AppliedCount: changed);
                return value;
            }, ct, preview.ResetEpoch);
            var active = preview.Owner == ProfileKey && preview.Generation == Generation && (api?.Session.FamilyId == preview.FamilyId);
            if (active) { Profile = copy; Error = null; }
            committed = true;
            return new(copy.ImportReceipts[receiptKey].AppliedCount, backupKey, active);
        }
        finally { gate.Release(); Notify(); if (committed) WakeRuntime(); }
    }

    public void EnsureTransferScope(string owner, long generation, Guid? family, bool requireAccount)
    {
        if (owner != ProfileKey || generation != Generation || api?.Session.FamilyId != family || api?.Transitioning == true
            || api?.Session.Authenticated == true && OwnerKey(api.Session) != owner || requireAccount && !Authenticated)
            throw new BrowserApiException(409, "account_changed", "Профиль изменился. Откройте просмотр переноса заново.");
    }
    public static void ValidateTransferBackup(TransferBackup? backup, string owner, Guid? id = null)
    {
        if (backup is null || backup.Owner != owner || backup.Id == Guid.Empty || id is not null && backup.Id != id
            || backup.Payload.Length == 0 || LegacyTransferCodec.Digest(Encoding.UTF8.GetBytes(backup.Payload)) != backup.Sha256)
            throw new InvalidDataException("Резервную копию не удалось проверить. Данные не изменены.");
        var contents = JsonSerializer.Deserialize<TransferBackupData>(backup.Payload, BrowserStorage.Json);
        if (contents is null || contents.Owner != owner || contents.Records is null || contents.Receipts is null)
            throw new InvalidDataException("Резервная копия повреждена. Данные не изменены.");
    }
}
