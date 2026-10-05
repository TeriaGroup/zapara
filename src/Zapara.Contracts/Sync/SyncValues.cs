using System.Text.Json.Serialization;

namespace Zapara.Contracts.Sync;

public abstract record SyncValue
{
    private protected SyncValue() { }
    public override string ToString() => "SyncValue { [REDACTED] }";
}

public sealed record HomeworkValue : SyncValue
{
    [JsonConstructor]
    public HomeworkValue(string subjectRaw, string subjectKey, string text, int targetNthOccurrence,
        DateTimeOffset createdAtUtc, DateOnly? legacyCreatedLocalDate)
    {
        SubjectRaw = SyncValidation.Text(subjectRaw, 256);
        SubjectKey = SyncValidation.SubjectKey(subjectRaw, subjectKey);
        Text = SyncValidation.Text(text, 4000);
        TargetNthOccurrence = SyncValidation.Range(targetNthOccurrence, 1, 10);
        CreatedAtUtc = SyncValidation.Utc(createdAtUtc);
        LegacyCreatedLocalDate = legacyCreatedLocalDate;
    }
    [JsonRequired, JsonInclude, JsonPropertyOrder(0)] public string SubjectRaw { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(1)] public string SubjectKey { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(2)] public string Text { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(3)] public int TargetNthOccurrence { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(4)] public DateTimeOffset CreatedAtUtc { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(5)] public DateOnly? LegacyCreatedLocalDate { get; private init; }
    public override string ToString() => "HomeworkValue { [REDACTED] }";
}

public sealed record CompletionValue : SyncValue
{
    [JsonConstructor]
    public CompletionValue(bool done, DateTimeOffset? doneAtUtc)
        => (Done, DoneAtUtc) = (done, doneAtUtc is null ? null : SyncValidation.Utc(doneAtUtc.Value));
    [JsonRequired, JsonInclude, JsonPropertyOrder(0)] public bool Done { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(1)] public DateTimeOffset? DoneAtUtc { get; private init; }
    public override string ToString() => "CompletionValue { [REDACTED] }";
}

public sealed record OverrideValue : SyncValue
{
    [JsonConstructor]
    public OverrideValue(string subjectRaw, string subjectKey, string scope, string displayName, string? note, DateTimeOffset createdAtUtc)
    {
        SubjectRaw = SyncValidation.Text(subjectRaw, 256);
        SubjectKey = SyncValidation.SubjectKey(subjectRaw, subjectKey);
        Scope = SyncValidation.Scope(scope);
        DisplayName = SyncValidation.Text(displayName, 256);
        Note = SyncValidation.Optional(note, 4000);
        CreatedAtUtc = SyncValidation.Utc(createdAtUtc);
    }
    [JsonRequired, JsonInclude, JsonPropertyOrder(0)] public string SubjectRaw { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(1)] public string SubjectKey { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(2)] public string Scope { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(3)] public string DisplayName { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(4)] public string? Note { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(5)] public DateTimeOffset CreatedAtUtc { get; private init; }
    public override string ToString() => "OverrideValue { [REDACTED] }";
}

public sealed record FriendValue : SyncValue
{
    [JsonConstructor]
    public FriendValue(string? groupId, string groupName, string memberNames, int paletteIndex, bool enabled)
        => (GroupId, GroupName, MemberNames, PaletteIndex, Enabled) = (SyncValidation.Optional(groupId, 64),
            SyncValidation.Text(groupName, 256), SyncValidation.Text(memberNames, 4000), SyncValidation.Range(paletteIndex, 1, 5), enabled);
    [JsonRequired, JsonInclude, JsonPropertyOrder(0)] public string? GroupId { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(1)] public string GroupName { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(2)] public string MemberNames { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(3)] public int PaletteIndex { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(4)] public bool Enabled { get; private init; }
    public override string ToString() => "FriendValue { [REDACTED] }";
}

public sealed record SettingsValue : SyncValue
{
    [JsonConstructor]
    public SettingsValue(string? selectedGroupId, bool parityInvert, string? notifyTime1, string? notifyTime2, int strictness, bool alwaysShow)
        => (SelectedGroupId, ParityInvert, NotifyTime1, NotifyTime2, Strictness, AlwaysShow) =
            (SyncValidation.Optional(selectedGroupId, 64), parityInvert, SyncValidation.Time(notifyTime1),
                SyncValidation.Time(notifyTime2), SyncValidation.Range(strictness, 0, 100), alwaysShow);
    [JsonRequired, JsonInclude, JsonPropertyOrder(0)] public string? SelectedGroupId { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(1)] public bool ParityInvert { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(2)] public string? NotifyTime1 { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(3)] public string? NotifyTime2 { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(4)] public int Strictness { get; private init; }
    [JsonRequired, JsonInclude, JsonPropertyOrder(5)] public bool AlwaysShow { get; private init; }
    public override string ToString() => "SettingsValue { [REDACTED] }";
}
