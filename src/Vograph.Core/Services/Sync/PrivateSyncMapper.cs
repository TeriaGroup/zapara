using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

internal static class PrivateSyncMapper
{
    private static readonly string[] Palette =
        ["#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C"];

    public static HomeworkValue Homework(string subjectRaw, string text, int nth, DateTimeOffset createdAtUtc, DateOnly? legacy)
    {
        var raw = string.IsNullOrWhiteSpace(subjectRaw) ? subjectRaw : subjectRaw.Trim();
        var key = SyncValidation.NormalizeSubject(raw);
        return new HomeworkValue(raw, key, text, nth, createdAtUtc, legacy);
    }

    public static CompletionValue Completion(bool done, DateTimeOffset? doneAtUtc) => new(done, doneAtUtc);

    public static OverrideValue Override(string subjectRaw, string scope, string displayName, string? note, DateTimeOffset createdAtUtc)
    {
        var raw = string.IsNullOrWhiteSpace(subjectRaw) ? subjectRaw : subjectRaw.Trim();
        return new OverrideValue(raw, SyncValidation.NormalizeSubject(raw), scope, displayName, note, createdAtUtc);
    }

    public static FriendValue Friend(string? groupId, string groupName, string memberNames, string? colorHex, bool enabled)
        => new(groupId, groupName, memberNames ?? "", PaletteIndex(colorHex), enabled);

    public static SettingsValue Settings(Models.Settings s)
        => new(s.MyGroupId, s.ParityInvert, s.NotifyTime1, s.NotifyTime2, s.IntersectionStrictness, s.AlwaysShowAllTrafficLights);

    public static int PaletteIndex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return 1;
        var h = hex.Trim();
        if (h.Length == 9 && h[0] == '#') h = "#" + h[3..];
        var i = Array.FindIndex(Palette, x => x.Equals(h, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i + 1 : 1;
    }

    public static (DateTimeOffset Utc, DateOnly? Legacy, DateTime Local) SplitCreated(DateTime? createdAt)
    {
        var local = createdAt ?? DateTime.UtcNow;
        if (local.Kind == DateTimeKind.Utc)
            return (new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc), TimeSpan.Zero), null, local);
        if (local.Kind == DateTimeKind.Local)
            return (local.ToUniversalTime(), DateOnly.FromDateTime(local), local);
        return (DateTimeOffset.UtcNow, DateOnly.FromDateTime(local), local);
    }
}
