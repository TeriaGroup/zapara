using Vograph.Core.Models;

namespace Vograph.Timetable;

public sealed class VoenmehScheduleClient
{
    public const string Origin = "https://voenmeh.ru/schedule";
    public const string MetaUrl = "https://voenmeh.ru/api/schedule/meta";
    public const string LessonsUrl = "https://voenmeh.ru/api/schedule/lessons";

    private readonly Func<string, CancellationToken, Task<string>> _get;

    public VoenmehScheduleClient(Func<string, CancellationToken, Task<string>> get) => _get = get;

    public Task<ParsedSchedule> FetchAsync(IReadOnlyList<string> groupNames, CancellationToken ct = default)
        => FetchAsync(groupNames, meta: null, ct);

    public async Task<ParsedSchedule> FetchAsync(IReadOnlyList<string> groupNames, VoenmehScheduleMeta? meta, CancellationToken ct)
    {
        meta ??= VoenmehScheduleParser.ParseMeta(await _get(MetaUrl, ct).ConfigureAwait(false));
        var fetchList = groupNames.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var loaded = new List<(string Name, List<Lesson> Lessons)>(fetchList.Count);
        using var gate = new SemaphoreSlim(6);
        var tasks = fetchList.Select(async name =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var url = $"{LessonsUrl}?name={Uri.EscapeDataString(name)}&type=group";
                var lessons = VoenmehScheduleParser.ParseLessons(await _get(url, ct).ConfigureAwait(false), name);
                return (name, lessons);
            }
            finally { gate.Release(); }
        });
        loaded.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
        return VoenmehScheduleParser.Assemble(meta, loaded);
    }
}
