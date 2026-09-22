using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zapara.Client.Domain;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public static partial class LegacyTransferCodec
{
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 32, PropertyNameCaseInsensitive = false, WriteIndented = true };
    private static readonly string[] Palette = ["#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C"];
    private static readonly Dictionary<string, int> OldPalette = new(StringComparer.OrdinalIgnoreCase)
    { ["#6CA5E0"] = 3, ["#98C379"] = 2, ["#E06C75"] = 5, ["#C678DD"] = 4, ["#F2C55C"] = 1 };

    public static TransferSource Parse(ReadOnlyMemory<byte> bytes, DateTimeOffset importedAt)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid("Размер файла должен быть не больше 16 МиБ.");
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes.Span).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(text, new() { MaxDepth = 32 });
            Unique(document.RootElement);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number) || number != 1)
                throw Invalid("Нужен файл переноса Запары версии 1.");
            foreach (var name in new[] { "ExportedAt", "Overrides", "Homework", "Friends", "Settings" })
                if (!root.TryGetProperty(name, out _)) throw Invalid("В файле отсутствуют обязательные разделы.");
            var payload = root.Deserialize<LegacyTransferPayload>(Json) ?? throw Invalid();
            _ = Date(payload.ExportedAt, importedAt);
            if (payload.Overrides is null || payload.Homework is null || payload.Friends is null || payload.Settings is null
                || payload.Overrides.Count + payload.Homework.Count + payload.Friends.Count > 10000) throw Invalid();
            var records = new List<TransferRecord>();
            var warnings = new List<string>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            string Key(string type, object value)
            {
                var canonical = type + ":" + Digest(JsonSerializer.SerializeToUtf8Bytes(value));
                var ordinal = occurrences.GetValueOrDefault(canonical);
                occurrences[canonical] = ordinal + 1;
                return "file:" + canonical + ":" + ordinal.ToString(CultureInfo.InvariantCulture);
            }
            foreach (var value in payload.Overrides)
            {
                if (value is null) throw Invalid();
                var date = Date(value.CreatedAt, importedAt);
                var raw = Text(value.SubjectRawNormalized, 256);
                var converted = new OverrideValue(raw, SyncValidation.NormalizeSubject(raw), value.Scope, Text(value.DisplayName, 256), value.Note, date.Utc);
                var key = Key("override", new { SubjectRawNormalized = converted.SubjectKey, value.Scope, value.DisplayName, value.Note, value.CreatedAt });
                records.Add(new(key, "override", Id(key), converted, converted.DisplayName, value.CreatedAt));
            }
            foreach (var value in payload.Homework)
            {
                if (value is null || value.Status is not ("pending" or "far" or "approaching" or "burning" or "burning_urgent" or "done" or "overdue")) throw Invalid();
                var date = Date(value.CreatedAt, importedAt);
                var raw = Text(value.SubjectRawNormalized, 256);
                var converted = new HomeworkValue(raw, SyncValidation.NormalizeSubject(raw), Text(value.Text, 4000), value.TargetNthOccurrence, date.Utc, date.Legacy);
                if (value.DueDateComputed is not null) _ = Date(value.DueDateComputed, importedAt);
                var done = value.DoneAt is null ? (DateTimeOffset?)null : Date(value.DoneAt, importedAt).Utc;
                // Computed status/deadline and completion are not homework identity. Occurrence
                // disambiguates genuinely distinct identical rows in the same legacy file.
                var key = Key("homework", new { SubjectRawNormalized = converted.SubjectKey, value.Text, value.CreatedAt, value.TargetNthOccurrence });
                var id = Id(key);
                records.Add(new(key, "homework", id, converted, raw + ": " + value.Text, value.CreatedAt, LegacyDueDate: value.DueDateComputed));
                var completion = new CompletionValue(value.Status == "done", done);
                var completionKey = key + ":completion:" + Digest(JsonSerializer.SerializeToUtf8Bytes(new { completion.Done, value.DoneAt }));
                records.Add(new(completionKey, "completion", id, completion, raw, LegacyDoneAt: value.DoneAt));
            }
            foreach (var value in payload.Friends)
            {
                if (value is null) throw Invalid();
                var palette = Color(value.ColorHex, out var fallback);
                if (fallback && !warnings.Contains("Некоторые цвета групп заменены ближайшим доступным цветом.")) warnings.Add("Некоторые цвета групп заменены ближайшим доступным цветом.");
                var converted = new FriendValue(null, Text(value.GroupName, 256), value.MemberNames, palette, value.Enabled);
                var key = Key("friend", new { converted.GroupName, converted.PaletteIndex, converted.Enabled, converted.MemberNames });
                records.Add(new(key, "friend", Id(key), converted, value.GroupName + (string.IsNullOrEmpty(value.MemberNames) ? "" : " · " + value.MemberNames)));
            }
            var settings = payload.Settings;
            var settingsValue = new SettingsValue(settings.MyGroupId, settings.ParityInvert, settings.NotifyTime1, settings.NotifyTime2,
                settings.IntersectionStrictness, settings.AlwaysShowAllTrafficLights);
            var settingsKey = "file:settings:" + ValueDigest(settingsValue);
            records.Add(new(settingsKey, "settings", SyncValidation.SettingsId, settingsValue, "Группа, чётность и напоминания"));
            if (settings.Language != "ru") warnings.Add("Язык интерфейса останется русским.");
            if (records.Any(record => record.Value is HomeworkValue { LegacyCreatedLocalDate: not null }))
                warnings.Add("Для старых заданий сохранён исходный календарный день создания.");
            return new("file", records, warnings);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or OverflowException)
        { throw Invalid("Файл повреждён или содержит неподдерживаемые значения. Данные не изменены."); }
    }

    public static TransferSource FromProfile(WebProfile profile, string kind)
    {
        var records = new List<TransferRecord>();
        var warnings = new List<string>();
        foreach (var entity in profile.Records.Values.Where(value => !value.Tombstone).OrderBy(value => value.EntityType).ThenBy(value => value.EntityId))
        {
            if (entity.EntityId == Guid.Empty || entity.Value is null) throw Invalid();
            var value = Decode(entity);
            if (value is CompletionValue && (!profile.Records.TryGetValue(ProfileValues.Key("homework", entity.EntityId), out var parent) || parent.Tombstone))
            { warnings.Add("Отдельная отметка выполнения без задания пропущена."); continue; }
            var provenance = Receipt(profile, entity.EntityType, entity.EntityId);
            if (provenance?.ProvenanceFingerprint != Provenance(value)) provenance = null;
            records.Add(new(kind + ":" + entity.EntityType + ":" + entity.EntityId.ToString("D") + ":" + ValueDigest(value),
                entity.EntityType, entity.EntityId, value, Label(value), provenance?.LegacyCreatedAt, provenance?.LegacyDoneAt, provenance?.LegacyDueDate));
        }
        return new(kind, records, warnings.Distinct().ToArray());
    }

    public static byte[] Export(WebProfile profile, DateTimeOffset exportedAt, ScheduleSnapshot? schedule = null, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var settings = profile.Records.GetValueOrDefault(ProfileValues.Key("settings", SyncValidation.SettingsId)) is { Tombstone: false } item
            ? ProfileValues.Read<SettingsValue>(item)! : ProfileValues.DefaultSettings;
        var payload = new LegacyTransferPayload
        {
            ExportedAt = exportedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Settings = new() { MyGroupId = settings.SelectedGroupId, ParityInvert = settings.ParityInvert, NotifyTime1 = settings.NotifyTime1,
                NotifyTime2 = settings.NotifyTime2, IntersectionStrictness = settings.Strictness, AlwaysShowAllTrafficLights = settings.AlwaysShow,
                Language = "ru", WeekCount = schedule?.WeekCount ?? 2, PeriodStart = schedule?.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
        };
        foreach (var entity in profile.Records.Values.Where(value => !value.Tombstone).OrderBy(value => value.EntityId))
        {
            var receipt = Receipt(profile, entity.EntityType, entity.EntityId);
            var currentValue = Decode(entity);
            if (receipt?.ProvenanceFingerprint != Provenance(currentValue)) receipt = null;
            switch (currentValue)
            {
                case HomeworkValue homework:
                    var completionRow = profile.Records.GetValueOrDefault(ProfileValues.Key("completion", entity.EntityId));
                    var completion = completionRow is { Tombstone: false } ? ProfileValues.Read<CompletionValue>(completionRow) : null;
                    var completedReceipt = Receipt(profile, "completion", entity.EntityId);
                    if (completion is not null && completedReceipt?.ProvenanceFingerprint != Provenance(completion)) completedReceipt = null;
                    var due = schedule is not null && settings.SelectedGroupId is not null
                        ? HomeworkRules.DueDate(schedule, settings.SelectedGroupId, homework, zone, settings.ParityInvert) : null;
                    var status = completion?.Done == true ? "done" : due is null ? "pending"
                        : HomeworkRules.Status(schedule!, settings.SelectedGroupId!, homework.SubjectRaw, due, false, TimeZoneInfo.ConvertTime(exportedAt, zone).Date, settings.ParityInvert);
                    payload.Homework.Add(new()
                    {
                        SubjectRawNormalized = homework.SubjectKey, Text = homework.Text, TargetNthOccurrence = homework.TargetNthOccurrence,
                        CreatedAt = receipt?.LegacyCreatedAt ?? (homework.LegacyCreatedLocalDate is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00" : homework.CreatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                        DueDateComputed = due?.ToString("O", CultureInfo.InvariantCulture) ?? receipt?.LegacyDueDate, Status = status,
                        DoneAt = completion?.DoneAtUtc is null ? null : completedReceipt?.LegacyDoneAt ?? completion.DoneAtUtc.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                    });
                    break;
                case OverrideValue rename:
                    payload.Overrides.Add(new() { SubjectRawNormalized = rename.SubjectKey, Scope = rename.Scope, DisplayName = rename.DisplayName,
                        Note = rename.Note, CreatedAt = receipt?.LegacyCreatedAt ?? rename.CreatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) });
                    break;
                case FriendValue friend:
                    payload.Friends.Add(new() { GroupName = friend.GroupName, ColorHex = Palette[friend.PaletteIndex - 1], Enabled = friend.Enabled, MemberNames = friend.MemberNames });
                    break;
            }
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        if (bytes.Length > MaximumBytes) throw Invalid("Копия слишком большая для переноса одним файлом: ограничение 16 МиБ.");
        return bytes;
    }

    public static SyncValue Decode(LocalEntity record) => record.EntityType switch
    {
        "homework" => ProfileValues.Read<HomeworkValue>(record) ?? throw Invalid(), "completion" => ProfileValues.Read<CompletionValue>(record) ?? throw Invalid(),
        "override" => ProfileValues.Read<OverrideValue>(record) ?? throw Invalid(), "friend" => ProfileValues.Read<FriendValue>(record) ?? throw Invalid(),
        "settings" => ProfileValues.Read<SettingsValue>(record) ?? throw Invalid(), _ => throw Invalid()
    };
    public static string ValueDigest(SyncValue value) => Digest(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), SyncJson.CreateOptions()));
    internal static string Provenance(SyncValue value) => value switch
    {
        HomeworkValue h => Digest(JsonSerializer.SerializeToUtf8Bytes(new { h.CreatedAtUtc, h.LegacyCreatedLocalDate })),
        OverrideValue o => Digest(JsonSerializer.SerializeToUtf8Bytes(o.CreatedAtUtc)),
        CompletionValue c => Digest(JsonSerializer.SerializeToUtf8Bytes(new { c.Done, c.DoneAtUtc })),
        _ => ""
    };
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static string Label(SyncValue value) => value switch
    {
        HomeworkValue h => h.SubjectRaw + ": " + h.Text, OverrideValue o => o.SubjectRaw + " → " + o.DisplayName,
        FriendValue f => f.GroupName + " · " + f.MemberNames,
        SettingsValue s => "Группа: " + (s.SelectedGroupId ?? "не выбрана") + "; напоминания: " + (s.NotifyTime1 ?? "выключено") + " и " + (s.NotifyTime2 ?? "выключено") + "; инверсия: " + (s.ParityInvert ? "да" : "нет") + "; строгость: " + s.Strictness,
        CompletionValue c => c.Done ? "Задание выполнено" : "Задание не выполнено", _ => "Запись"
    };
    internal static TransferReceipt? Receipt(WebProfile profile, string type, Guid id) => profile.ImportReceipts.Values.LastOrDefault(value => value.EntityType == type && value.EntityId == id);
    private static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    private static string Text(string value, int maximum) => string.IsNullOrWhiteSpace(SyncValidation.Text(value, maximum)) ? throw Invalid() : value;
    private static (DateTimeOffset Utc, DateOnly? Legacy) Date(string value, DateTimeOffset anchor)
    {
        if (value is null || value.Length is < 10 or > 40 || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date == DateTimeOffset.MinValue || !DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) throw Invalid();
        if (value.EndsWith('Z')) return (date.ToUniversalTime(), null);
        var explicitOffset = value.Length > 10 && (value[10..].Contains('+') || value[10..].Contains('-'));
        return (explicitOffset ? date.ToUniversalTime() : anchor.ToUniversalTime(), day);
    }
    private static int Color(string? input, out bool fallback)
    {
        var value = input?.Trim() ?? "";
        if (value.Length == 9 && value[0] == '#') value = "#" + value[3..];
        var index = Array.FindIndex(Palette, color => color.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) { fallback = false; return index + 1; }
        if (OldPalette.TryGetValue(value, out var old)) { fallback = false; return old; }
        fallback = true; return 1;
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw Invalid("Файл содержит повторяющиеся поля. Данные не изменены."); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) Unique(value);
    }
    private static InvalidDataException Invalid(string message = "Некорректный файл переноса. Данные не изменены.") => new(message);
}
