using System.Globalization;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Week;

public sealed record WeekCalendarBatch(IReadOnlyList<CalendarExportEntry> Entries, int SkippedCount);

public static class WeekCalendarEntries
{
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static WeekCalendarBatch Create(IEnumerable<WeekDay> days, string groupId)
    {
        var entries = new List<CalendarExportEntry>();
        var skipped = 0;
        foreach (var day in days)
        foreach (var row in day.Rows)
        {
            if (!TimeSpan.TryParseExact(row.Time, @"hh\:mm", CultureInfo.InvariantCulture, out var start) ||
                !TimeSpan.TryParseExact(row.TimeEnd, @"hh\:mm", CultureInfo.InvariantCulture, out var end) ||
                end <= start || string.IsNullOrWhiteSpace(row.Name))
            { skipped++; continue; }
            var startAt = new DateTimeOffset(DateTime.SpecifyKind(day.Date.Date.Add(start), DateTimeKind.Unspecified), MoscowOffset);
            var endAt = new DateTimeOffset(DateTime.SpecifyKind(day.Date.Date.Add(end), DateTimeKind.Unspecified), MoscowOffset);
            var identity = CalendarExport.CanonicalId(groupId, row.SubjectRaw, row.Teacher, row.ClassroomRaw);
            var description = string.Join(" · ", new[] { row.TypeLabel, row.Teacher }.Where(value => !string.IsNullOrWhiteSpace(value)));
            entries.Add(new CalendarExportEntry(identity, startAt, endAt,
                row.Name, string.IsNullOrWhiteSpace(row.Room) ? null : row.Room, description.Length == 0 ? null : description));
        }
        return new(entries, skipped);
    }
}
