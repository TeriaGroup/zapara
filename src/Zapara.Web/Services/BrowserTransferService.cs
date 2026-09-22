using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Zapara.Web.Services;

public sealed class BrowserTransferService(WebAppState state, BrowserStorage storage, BrowserApiClient api, IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    public TransferPreview? Preview { get; private set; }
    public IReadOnlyList<TransferBackupInfo> Backups => state.Profile.ImportReceipts.Values.Where(value => value.BackupKey is not null && value.AppliedAt is not null)
        .Select(value => new TransferBackupInfo(value.BackupKey!, value.AppliedAt!.Value)).DistinctBy(value => value.Key).OrderByDescending(value => value.CreatedAt).ToArray();

    public async Task PreviewGuestAsync(CancellationToken ct = default)
    {
        var owner = state.ProfileKey; var generation = state.Generation; var family = api.Session.FamilyId;
        state.EnsureTransferScope(owner, generation, family, true);
        if (!state.Profile.HasSnapshot)
        {
            await state.SynchronizeAsync(ct);
            state.EnsureTransferScope(owner, generation, family, true);
            if (!state.Profile.HasSnapshot) throw new InvalidOperationException("Дождитесь первой успешной синхронизации аккаунта, затем откройте перенос гостевых записей.");
        }
        var guest = await storage.ReadAsync<WebProfile>("profiles", "guest") ?? new();
        if (guest.Owner != "guest") throw new InvalidDataException("Гостевой профиль повреждён.");
        Preview = await state.PreviewTransferAsync(LegacyTransferCodec.FromProfile(guest, "guest"), owner, generation, family, ct);
    }

    public async Task PreviewFileAsync(IBrowserFile file, CancellationToken ct = default)
    {
        var owner = state.ProfileKey; var generation = state.Generation; var family = api.Session.FamilyId;
        if (file.Size is <= 0 or > LegacyTransferCodec.MaximumBytes) throw new InvalidDataException("Выберите JSON-файл не больше 16 МиБ.");
        await using var stream = file.OpenReadStream(LegacyTransferCodec.MaximumBytes, ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (output.Length + read > LegacyTransferCodec.MaximumBytes) throw new InvalidDataException("Файл больше 16 МиБ. Данные не изменены.");
            output.Write(buffer, 0, read);
        }
        var source = LegacyTransferCodec.Parse(output.ToArray(), DateTimeOffset.UtcNow);
        Preview = await state.PreviewTransferAsync(source, owner, generation, family, ct);
    }

    public async Task PreviewBackupAsync(string key, CancellationToken ct = default)
    {
        var owner = state.ProfileKey; var generation = state.Generation; var family = api.Session.FamilyId;
        var (backup, profile) = await ReadBackup(key, owner);
        Preview = await state.PreviewTransferAsync(LegacyTransferCodec.FromProfile(profile, "backup:" + backup.Id.ToString("D")), owner, generation, family, ct);
    }

    public async Task<TransferApplyResult> ApplyAsync(CancellationToken ct = default)
    {
        var preview = Preview ?? throw new InvalidOperationException("Сначала откройте просмотр переноса.");
        var result = await state.ApplyTransferAsync(preview, ct);
        Preview = null;
        return result;
    }

    public async Task ExportAsync(CancellationToken ct = default)
    {
        var owner = state.ProfileKey; var generation = state.Generation; var family = api.Session.FamilyId;
        var profile = await storage.ReadAsync<WebProfile>("profiles", owner) ?? new() { Owner = owner };
        state.EnsureTransferScope(owner, generation, family, false);
        var bytes = LegacyTransferCodec.Export(profile, DateTimeOffset.UtcNow, state.Schedule);
        await (await Module()).InvokeVoidAsync("download", ct, bytes, "zapara-transfer-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
    }
    public async Task DownloadBackupAsync(string key, CancellationToken ct = default)
    {
        var owner = state.ProfileKey; var generation = state.Generation; var family = api.Session.FamilyId;
        var (backup, profile) = await ReadBackup(key, owner);
        state.EnsureTransferScope(owner, generation, family, false);
        await (await Module()).InvokeVoidAsync("download", ct, LegacyTransferCodec.Export(profile, backup.CreatedAt), "zapara-backup-" + backup.Id.ToString("D") + ".json");
    }
    public void ClearPreview() => Preview = null;
    private async Task<(TransferBackup Backup, WebProfile Profile)> ReadBackup(string key, string owner)
    {
        if (!key.StartsWith("backup:" + owner + ":", StringComparison.Ordinal)) throw new InvalidDataException("Копия принадлежит другому профилю.");
        var backup = await storage.ReadAsync<TransferBackup>("profiles", key);
        WebAppState.ValidateTransferBackup(backup, owner);
        var data = JsonSerializer.Deserialize<TransferBackupData>(backup!.Payload, BrowserStorage.Json)!;
        return (backup, new() { Owner = owner, Records = data.Records, ImportReceipts = data.Receipts });
    }
    private Task<IJSObjectReference> Module() => module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/transfer.js").AsTask();
    public async ValueTask DisposeAsync() { if (module is { IsCompletedSuccessfully: true }) await module.Result.DisposeAsync(); }
}
