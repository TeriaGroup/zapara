using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Teachers;
using Zapara.Client.Domain;

namespace Zapara.Web.Services;

public sealed record LecturerSourceMetadata(string Source, DateTimeOffset? FetchedAt, DateTimeOffset? LastAttemptAt, string? LastFailure, string CacheLifetime);
public sealed record LecturerCatalogEnvelope(string Version, IReadOnlyList<LecturerInfo> Lecturers, LecturerSourceMetadata? Metadata = null);
public sealed record LecturerTimetableEnvelope(string Version, LecturerInfo Lecturer, IReadOnlyList<LecturerLesson> Lessons, LecturerSourceMetadata? Metadata = null);

/// <summary>Public catalogs only. Failed refreshes never replace the last usable catalog or timetable.</summary>
public sealed class BrowserLecturerStore(HttpClient http, BrowserStorage storage)
{
    private Task? initialization;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private LecturerCatalog? fallback;
    private LecturerCatalogEnvelope? catalog;
    private TeacherIndex index = new([], []);
    private Dictionary<string, LecturerInfo[]> nameCandidates = new(StringComparer.OrdinalIgnoreCase);
    private string[]? mineNames;
    private HashSet<string> mineIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LecturerTimetableEnvelope> details = [];
    private readonly Dictionary<string, string> detailNotices = [];
    public event Action? Changed;
    public IReadOnlyList<LecturerInfo> Lecturers => index.Lecturers;
    public bool Loading { get; private set; }
    public bool Refreshing { get; private set; }
    public string? Notice { get; private set; }
    public string? Error { get; private set; }
    public string? FreshnessNotice => catalog?.Metadata switch
    {
        { Source: "packaged" } => "Встроенный снимок сервера: загрузка актуального справочника университета пока не подтверждена." + FailureNotice,
        { Source: "university", FetchedAt: { } fetched } => "Данные университета получены " + fetched.ToUniversalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC." + FailureNotice,
        _ => catalog is null && fallback is not null ? "Встроенный снимок приложения. Свежие данные пока недоступны." :
            catalog is not null ? "Сохранённый справочник. Время загрузки университетского источника неизвестно." : null
    };
    private string FailureNotice => catalog?.Metadata?.LastFailure is not null
        ? " Последняя проверка не удалась; сохранена последняя корректная версия." : "";

    public Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        Loading = true;
        Changed?.Invoke();
        try
        {
            try
            {
                var saved = await storage.ReadAsync<LecturerCatalogEnvelope>("public", "lecturers:catalog");
                if (saved is not null) { Validate(saved); catalog = saved; }
            }
            catch (Exception e) when (e is JSException or JsonException or InvalidDataException) { }
            RebuildIndex();
        }
        finally { Loading = false; Changed?.Invoke(); }
        await RefreshAsync();
        if (catalog is null) await EnsureBundleAsync();
    }

    private async Task EnsureBundleAsync()
    {
        if (fallback is not null || catalog is not null) return;
        try
        {
            var xml = await storage.ReadAsync<string>("public", "lecturers:xml");
            if (xml is null)
            {
                xml = await ReadAsync("data/TimetableLecturer50.xml", 16 * 1024 * 1024, CancellationToken.None);
                await PersistAsync("lecturers:xml", xml);
            }
            fallback = ParseBundle(xml);
            RebuildIndex();
            Changed?.Invoke();
        }
        catch (Exception e) when (IsLoadFailure(e) || e is JSException) { }
    }

    public async Task RefreshAsync()
    {
        if (!await refreshGate.WaitAsync(0)) return;
        Refreshing = true;
        Changed?.Invoke();
        try
        {
            var next = JsonSerializer.Deserialize<LecturerCatalogEnvelope>(await ReadAsync("/api/v1/teachers", 2 * 1024 * 1024, CancellationToken.None), BrowserStorage.Json)
                ?? throw new InvalidDataException();
            Validate(next);
            Notice = null;
            await PersistAsync("lecturers:catalog", next);
            catalog = next;
            fallback = null;
            RebuildIndex();
            Error = null;
        }
        catch (Exception e) when (IsLoadFailure(e))
        {
            if (Lecturers.Count == 0) Error = "Не удалось загрузить преподавателей. Проверьте соединение и повторите попытку.";
            else Notice = "Не удалось обновить справочник. Показаны последние сохранённые данные.";
        }
        finally { Refreshing = false; refreshGate.Release(); Changed?.Invoke(); }
    }

    public IReadOnlyList<LecturerInfo> Search(string query, bool onlyMine, IEnumerable<Lesson> myLessons)
    {
        var mine = onlyMine ? MatchMyTeachers(myLessons) : new HashSet<string>();
        var matches = index.Filter(query, onlyMine, mine);
        var names = query.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0) return matches;
        var found = matches.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
            foreach (var lecturer in Candidates(name))
                if ((!onlyMine || mine.Contains(lecturer.Id)) && TeacherSearch.SameTeacher(lecturer.Name, name)) found.Add(lecturer.Id);
        return index.Lecturers.Where(lecturer => found.Contains(lecturer.Id)).ToArray();
    }

    public IReadOnlySet<string> MyTeacherIds(IEnumerable<Lesson> myLessons) => MatchMyTeachers(myLessons);
    private HashSet<string> MatchMyTeachers(IEnumerable<Lesson> lessons)
    {
        var names = lessons.Where(lesson => !string.IsNullOrWhiteSpace(lesson.TeacherRaw) && lesson.TeacherRaw != "—")
            .SelectMany(lesson => lesson.TeacherRaw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (mineNames is not null && mineNames.SequenceEqual(names, StringComparer.OrdinalIgnoreCase)) return mineIds;
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
            foreach (var lecturer in Candidates(name))
                if (TeacherSearch.SameTeacher(lecturer.Name, name)) matched.Add(lecturer.Id);
        mineNames = names; return mineIds = matched;
    }
    // SameTeacher requires equality of the first surname token. This cheap necessary
    // condition avoids repeating its regex/name parsing for all 700×group-name pairs.
    private LecturerInfo[] Candidates(string name) => nameCandidates.GetValueOrDefault(FirstToken(name)) ?? [];
    private static string FirstToken(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('.') ?? "";

    public IReadOnlyList<LecturerLesson> LessonsOf(string id, int parity = 0, bool invert = false)
    {
        var code = invert && parity != 0 ? parity == 1 ? 2 : 1 : parity;
        return index.LessonsOf(id).Where(l => code == 0 || l.Parity == 0 || l.Parity == code)
            .OrderBy(l => l.DayOfWeek).ThenBy(l => TimeSpan.TryParse(l.TimeStart, out var time) ? time : TimeSpan.MaxValue).ThenBy(l => l.Parity).ToArray();
    }

    public string? DetailNotice(string id) => detailNotices.GetValueOrDefault(id) ??
        (details.TryGetValue(id, out var saved) && saved.Version == catalog?.Version ?
            saved.Metadata?.Source == "packaged" ? "Расписание из встроенного снимка сервера." : null :
            details.ContainsKey(id) ? "Сохранённое расписание предыдущего обновления. Свежие данные пока недоступны." :
            fallback?.Lecturers.Any(l => l.Id == id) == true ? "Расписание из встроенного снимка. При подключении проверим обновления." : null);

    public async Task LoadLessonsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (id.Length is 0 or > 128 || Lecturers.All(l => l.Id != id)) return;
        if (!details.ContainsKey(id))
        {
            try
            {
                var saved = await storage.ReadAsync<LecturerTimetableEnvelope>("public", "lecturers:detail:" + id);
                cancellationToken.ThrowIfCancellationRequested();
                if (saved is not null) { Validate(saved, id); details[id] = saved; RebuildIndex(); Changed?.Invoke(); }
            }
            catch (Exception e) when (e is JSException or JsonException or InvalidDataException) { }
        }
        var expectedVersion = catalog?.Version;
        try
        {
            var next = JsonSerializer.Deserialize<LecturerTimetableEnvelope>(await ReadAsync("/api/v1/teachers/" + Uri.EscapeDataString(id) + "/timetable", 2 * 1024 * 1024, cancellationToken), BrowserStorage.Json)
                ?? throw new InvalidDataException();
            Validate(next, id);
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedVersion is null || catalog?.Version != expectedVersion || next.Version != expectedVersion) throw new InvalidDataException();
            await PersistAsync("lecturers:detail:" + id, next);
            cancellationToken.ThrowIfCancellationRequested();
            if (catalog?.Version != expectedVersion) return;
            details[id] = next;
            detailNotices.Remove(id);
            RebuildIndex();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (IsLoadFailure(e))
        {
            detailNotices[id] = index.LessonsOf(id).Count > 0
                ? "Не удалось обновить занятия. Показано последнее сохранённое расписание."
                : "Занятия пока недоступны. Подключитесь к сети и повторите попытку.";
        }
        finally { Changed?.Invoke(); }
    }

    private void RebuildIndex()
    {
        var lecturers = catalog?.Lecturers ?? fallback?.Lecturers ?? [];
        var known = lecturers.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var lessons = (catalog is null ? fallback?.Lessons ?? [] : Array.Empty<LecturerLesson>())
            .Where(l => known.Contains(l.LecturerId) && !details.ContainsKey(l.LecturerId))
            .Concat(details.Where(pair => known.Contains(pair.Key)).SelectMany(pair => pair.Value.Lessons)).ToArray();
        index = new(lecturers, lessons);
        nameCandidates = index.Lecturers.GroupBy(lecturer => FirstToken(lecturer.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        mineNames = null;
    }

    private async Task PersistAsync<T>(string key, T value)
    {
        try { await storage.WriteAsync("public", key, value); }
        catch (JSException) { Notice = "Данные загружены, но не сохранены для офлайна. Проверьте свободное место в браузере."; }
    }

    private static LecturerCatalog ParseBundle(string xml)
    {
        if (xml.Length > 16 * 1024 * 1024) throw new InvalidDataException();
        var parsed = LecturerCatalog.Parse(xml);
        if (parsed.Lecturers.Count == 0) throw new InvalidDataException();
        return parsed;
    }

    private static void Validate(LecturerCatalogEnvelope value)
    {
        if (string.IsNullOrWhiteSpace(value.Version) || value.Version.Length > 128 || value.Lecturers is null || value.Lecturers.Count is 0 or > 5000 ||
            value.Lecturers.Any(l => l is null || string.IsNullOrWhiteSpace(l.Id) || l.Id.Length > 128 || string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 512 || l.Kafedra is null) ||
            value.Lecturers.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count() != value.Lecturers.Count) throw new InvalidDataException();
    }

    private static void Validate(LecturerTimetableEnvelope value, string id)
    {
        Validate(new(value.Version, [value.Lecturer]));
        if (value.Lecturer.Id != id || value.Lessons is null || value.Lessons.Count > 10000 || value.Lessons.Any(l => l is null || l.LecturerId != id ||
            l.DayOfWeek is < 1 or > 6 || l.Parity is < 0 or > 2 || !TimeSpan.TryParse(l.TimeStart, out _) || !TimeSpan.TryParse(l.TimeEnd, out _) ||
            l.SubjectRaw is null || l.DisciplineRaw is null || l.TypeRaw is null || l.ClassroomRaw is null || l.Groups is null ||
            l.Groups.Any(g => g is null || g.IdGroup is null || g.Number is null))) throw new InvalidDataException();
    }

    private async Task<string> ReadAsync(string uri, int maximum, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > maximum) throw new InvalidDataException();
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }

    private static bool IsLoadFailure(Exception error) => error is HttpRequestException or JsonException or InvalidDataException or
        System.Xml.XmlException or ArgumentException or OperationCanceledException or IOException;
}
