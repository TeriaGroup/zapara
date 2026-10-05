using System.Text;
using Vograph.Core.Services;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public sealed class TransferPreviewRow
{
    public required TransferRecord Record { get; init; }
    public required Guid TargetId { get; init; }
    public Guid CopyId { get; init; } = Guid.NewGuid();
    public required string Kind { get; set; }
    public required string Signature { get; init; }
    public string? ExistingLabel { get; init; }
    public bool CanReplace { get; init; }
    public string Choice { get; set; } = "keep";
}
public sealed record TransferPreview(string Owner, long Generation, Guid? FamilyId, TransferSource Source, IReadOnlyList<TransferPreviewRow> Rows)
{
    public long ResetEpoch { get; init; }
    public int NewCount => Rows.Count(row => row.Kind == "new" && row.Record.EntityType != "completion");
    public int Conflicts => Rows.Count(row => row.Kind is "conflict" or "alternatives");
    public int AlreadyCount => Rows.Count(row => row.Kind is "already" or "same");
}
public sealed record TransferApplyResult(int Changed, string? BackupKey, bool ActiveProfile);
internal sealed record PreparedTransferRow(TransferPreviewRow Preview, Guid TargetId, ProfileChange? Change, TransferReceipt Receipt);

public static class TransferPlanner
{
    public static TransferPreview Create(TransferSource source, WebProfile target, long generation, Guid? family)
    {
        var rows = new List<TransferPreviewRow>();
        foreach (var record in source.Records.OrderBy(value => value.EntityType == "completion" ? 1 : 0))
        {
            var prior = target.ImportReceipts.GetValueOrDefault(record.SourceKey);
            var targetId = prior?.EntityId ?? record.EntityId;
            if (record.EntityType == "completion")
            {
                var parent = rows.FirstOrDefault(row => row.Record.EntityType == "homework" && row.Record.EntityId == record.EntityId);
                if (parent is not null) targetId = parent.TargetId;
            }
            var natural = record.Value is OverrideValue rename ? FindOverride(target, rename) : null;
            if (natural is not null) targetId = natural.EntityId;
            target.Records.TryGetValue(ProfileValues.Key(record.EntityType, targetId), out var existing);
            var kind = prior is not null ? "already" : record.EntityType == "settings" ? "settings"
                : existing is null ? "new" : Same(record.Value, existing) ? "same" : "conflict";
            var row = new TransferPreviewRow
            {
                Record = record, TargetId = targetId, Kind = kind, Signature = Signature(target, record, targetId),
                ExistingLabel = existing is { Tombstone: false } ? LegacyTransferCodec.Label(LegacyTransferCodec.Decode(existing)) : existing is null ? null : "Запись удалена в текущем профиле",
                CanReplace = record.EntityType is "settings" or "completion" || natural is not null,
                Choice = kind == "new" ? "add" : "keep"
            };
            rows.Add(row);
        }
        // A legacy file may itself contain competing names for the same subject/scope.
        // Require an explicit selection rather than install ambiguous active overrides.
        var names = rows.Where(row => row.Record.Value is OverrideValue).ToArray();
        foreach (var row in names.Where(row => row.Kind == "new"))
        {
            var current = (OverrideValue)row.Record.Value;
            if (names.Any(other => other != row && other.Record.Value is OverrideValue candidate
                && candidate.Scope == current.Scope && ParityService.SameSubject(candidate.SubjectRaw, current.SubjectRaw)))
            { row.Kind = "alternatives"; row.Choice = "keep"; }
        }
        return new(target.Owner, generation, family, source, rows) { ResetEpoch = target.ResetEpoch };
    }

    internal static IReadOnlyList<PreparedTransferRow> Prepare(TransferPreview preview, WebProfile latest)
    {
        var prepared = new List<PreparedTransferRow>();
        var parents = new Dictionary<Guid, Guid?>();
        foreach (var row in preview.Rows.OrderBy(value => value.Record.EntityType == "completion" ? 1 : 0))
        {
            var record = row.Record;
            if (latest.ImportReceipts.TryGetValue(record.SourceKey, out var already))
            {
                if (record.EntityType == "homework") parents[record.EntityId] = already.EntityId;
                continue;
            }
            var parentCopy = record.EntityType == "completion" && parents.TryGetValue(record.EntityId, out var pairedId) && pairedId is not null && pairedId != row.TargetId;
            var use = parentCopy || row.Kind == "same" || row.Choice is "add" or "copy" or "incoming";
            if (!use) { if (record.EntityType == "homework") parents[record.EntityId] = null; continue; }
            if (row.Choice == "incoming" && !row.CanReplace || row.Choice == "copy" && row.CanReplace || row.Choice == "add" && row.Kind is not ("new" or "alternatives"))
                throw new InvalidOperationException("Для этой записи выбран неподходящий способ переноса.");
            var id = row.Choice == "copy" ? row.CopyId : row.TargetId;
            if (record.EntityType == "homework") parents[record.EntityId] = id;
            if (record.EntityType == "completion")
            {
                if (!parents.TryGetValue(record.EntityId, out var parent) || parent is null) continue;
                id = parent.Value;
                if (!prepared.Any(item => item.Change?.EntityType == "homework" && item.TargetId == id)
                    && latest.Records.GetValueOrDefault(ProfileValues.Key("homework", id)) is not { Tombstone: false }) continue;
            }
            if (Signature(latest, record, row.TargetId) != row.Signature) throw Changed();
            if (id != row.TargetId && latest.Records.ContainsKey(ProfileValues.Key(record.EntityType, id))) throw Changed();
            SyncValue value = record.Value;
            var legacyCreated = record.LegacyCreatedAt;
            if ((row.Choice == "incoming" || row.Kind == "same") && value is OverrideValue rename && latest.Records.GetValueOrDefault(ProfileValues.Key("override", id)) is { Tombstone: false } target)
            {
                var original = LegacyTransferCodec.Decode(target) as OverrideValue ?? throw Changed();
                value = new OverrideValue(rename.SubjectRaw, rename.SubjectKey, rename.Scope, rename.DisplayName, rename.Note, original.CreatedAtUtc);
                legacyCreated = LegacyTransferCodec.Receipt(latest, "override", id)?.LegacyCreatedAt;
            }
            var change = row.Kind == "same" && !parentCopy ? null : new ProfileChange(record.EntityType, id, value);
            prepared.Add(new(row, id, change, new(record.EntityType, id, legacyCreated, record.LegacyDoneAt, record.LegacyDueDate,
                ProvenanceFingerprint: LegacyTransferCodec.Provenance(value))));
        }
        var friendCount = latest.Records.Values.Count(row => row.EntityType == "friend" && !row.Tombstone);
        friendCount += prepared.Count(row => row.Change?.EntityType == "friend" && !latest.Records.ContainsKey(ProfileValues.Key("friend", row.TargetId)));
        if (friendCount > 5) throw new InvalidOperationException("В профиле получится больше пяти групп друзей. Уберите лишние группы из переноса.");
        var imports = prepared.Where(row => row.Change?.Value is OverrideValue).ToArray();
        for (var index = 0; index < imports.Length; index++)
            for (var other = index + 1; other < imports.Length; other++)
            {
                var a = (OverrideValue)imports[index].Change!.Value!; var b = (OverrideValue)imports[other].Change!.Value!;
                if (a.Scope == b.Scope && ParityService.SameSubject(a.SubjectRaw, b.SubjectRaw))
                    throw new InvalidOperationException("В источнике несколько названий одного предмета. Выберите только один вариант для каждой области действия.");
            }
        return prepared;
    }

    internal static void Revalidate(TransferPreview preview, WebProfile latest, IReadOnlyList<PreparedTransferRow> prepared)
    {
        foreach (var row in prepared)
        {
            if (latest.ImportReceipts.ContainsKey(row.Preview.Record.SourceKey)) continue;
            if (Signature(latest, row.Preview.Record, row.Preview.TargetId) != row.Preview.Signature) throw Changed();
            if (row.TargetId != row.Preview.TargetId && latest.Records.ContainsKey(ProfileValues.Key(row.Preview.Record.EntityType, row.TargetId))) throw Changed();
        }
        var friends = latest.Records.Values.Count(row => row.EntityType == "friend" && !row.Tombstone)
            + prepared.Count(row => row.Change?.EntityType == "friend" && !latest.ImportReceipts.ContainsKey(row.Preview.Record.SourceKey)
                && !latest.Records.ContainsKey(ProfileValues.Key("friend", row.TargetId)));
        if (friends > 5) throw new InvalidOperationException("В другой вкладке добавлены группы друзей. Доступно не больше пяти групп; обновите просмотр переноса.");
    }
    private static LocalEntity? FindOverride(WebProfile profile, OverrideValue value) => profile.Records.Values.FirstOrDefault(record => record.EntityType == "override"
        && !record.Tombstone && LegacyTransferCodec.Decode(record) is OverrideValue existing && existing.Scope == value.Scope && ParityService.SameSubject(existing.SubjectRaw, value.SubjectRaw));
    private static bool Same(SyncValue value, LocalEntity existing)
    {
        if (existing.Tombstone) return false;
        if (value is OverrideValue a && LegacyTransferCodec.Decode(existing) is OverrideValue b)
            return a.Scope == b.Scope && ParityService.SameSubject(a.SubjectRaw, b.SubjectRaw) && a.DisplayName == b.DisplayName && a.Note == b.Note;
        return LegacyTransferCodec.ValueDigest(value) == LegacyTransferCodec.ValueDigest(LegacyTransferCodec.Decode(existing));
    }
    private static string Signature(WebProfile profile, TransferRecord source, Guid targetId)
    {
        string Entry(LocalEntity? value) => value is null ? "absent" : value.EntityId.ToString("D") + ":" + (value.Tombstone ? "deleted" : LegacyTransferCodec.ValueDigest(LegacyTransferCodec.Decode(value)));
        var material = Entry(profile.Records.GetValueOrDefault(ProfileValues.Key(source.EntityType, targetId)));
        if (source.Value is OverrideValue rename)
            material += "|" + string.Join("|", profile.Records.Values.Where(value => value.EntityType == "override" && !value.Tombstone
                && LegacyTransferCodec.Decode(value) is OverrideValue other && other.Scope == rename.Scope && ParityService.SameSubject(other.SubjectRaw, rename.SubjectRaw)).Select(Entry));
        if (source.EntityType == "completion") material += "|parent:" + Entry(profile.Records.GetValueOrDefault(ProfileValues.Key("homework", targetId)));
        return LegacyTransferCodec.Digest(Encoding.UTF8.GetBytes(material));
    }
    internal static InvalidOperationException Changed() => new("Записи, показанные в просмотре, изменились. Обновите просмотр и подтвердите перенос ещё раз.");
}
