using System.Text.Json;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public sealed record TransferReceipt(string EntityType, Guid EntityId, string? LegacyCreatedAt = null,
    string? LegacyDoneAt = null, string? LegacyDueDate = null, string? BackupKey = null, DateTimeOffset? AppliedAt = null, int AppliedCount = 0,
    string? ProvenanceFingerprint = null);
public sealed record TransferRecord(string SourceKey, string EntityType, Guid EntityId, SyncValue Value, string Label,
    string? LegacyCreatedAt = null, string? LegacyDoneAt = null, string? LegacyDueDate = null);
public sealed record TransferSource(string Kind, IReadOnlyList<TransferRecord> Records, IReadOnlyList<string> Warnings);
public sealed record TransferBackupData(string Owner, Dictionary<string, LocalEntity> Records, Dictionary<string, TransferReceipt> Receipts);
public sealed record TransferBackup(Guid Id, string Owner, DateTimeOffset CreatedAt, string Sha256, string Payload);
public sealed record TransferBackupInfo(string Key, DateTimeOffset CreatedAt);

public sealed class LegacyTransferPayload
{
    public int Version { get; set; } = 1;
    public string ExportedAt { get; set; } = "";
    public List<LegacyOverride> Overrides { get; set; } = [];
    public List<LegacyHomework> Homework { get; set; } = [];
    public List<LegacyFriend> Friends { get; set; } = [];
    public LegacySettings Settings { get; set; } = new();
}
public sealed class LegacyOverride
{
    public string SubjectRawNormalized { get; set; } = "";
    public string Scope { get; set; } = "global";
    public string DisplayName { get; set; } = "";
    public string? Note { get; set; }
    public string CreatedAt { get; set; } = "";
}
public sealed class LegacyHomework
{
    public string SubjectRawNormalized { get; set; } = "";
    public string Text { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public int TargetNthOccurrence { get; set; }
    public string? DueDateComputed { get; set; }
    public string Status { get; set; } = "pending";
    public string? DoneAt { get; set; }
}
public sealed class LegacyFriend
{
    public string GroupName { get; set; } = "";
    public string ColorHex { get; set; } = "#F2A33C";
    public bool Enabled { get; set; } = true;
    public string MemberNames { get; set; } = "";
}
public sealed class LegacySettings
{
    public string? MyGroupId { get; set; }
    public bool ParityInvert { get; set; }
    public string? NotifyTime1 { get; set; }
    public string? NotifyTime2 { get; set; }
    public int IntersectionStrictness { get; set; } = 25;
    public string Language { get; set; } = "ru";
    public string? LastSyncAt { get; set; }
    public string? LastFetchedAt { get; set; }
    public string? LastAutoCheckAt { get; set; }
    public int WeekCount { get; set; } = 2;
    public string? PeriodTitle { get; set; }
    public string? PeriodStart { get; set; }
    public int MapPanelWidth { get; set; } = 300;
    public bool AlwaysShowAllTrafficLights { get; set; }
}

public static partial class LegacyTransferCodec
{
    public const int MaximumBytes = 16 * 1024 * 1024;
}
