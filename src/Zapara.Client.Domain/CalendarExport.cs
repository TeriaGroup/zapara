using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zapara.Client.Domain;

public sealed record CalendarExportEntry(string Id, DateTimeOffset Start, DateTimeOffset End,
    string Summary, string? Location = null, string? Description = null);
public sealed record CalendarExportResult(string Content, int EventCount, int SkippedCount);
public sealed record AllDayCalendarEntry(string Id, DateOnly? Day, string Summary, string? Description = null);

/// <summary>Static RFC 5545 snapshot of exact occurrences; no recurring templates or storage side effects.</summary>
public static class CalendarExport
{
    public static CalendarExportResult CreateAllDay(IEnumerable<AllDayCalendarEntry> entries, string name, DateTimeOffset generatedAt)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Расписание военмех//Расписание//RU",
            "CALSCALE:GREGORIAN", "X-WR-CALNAME:" + Escape(name) };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0; var skipped = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Summary) || entry.Day is not { } day || day == DateOnly.MaxValue)
            { skipped++; continue; }
            var start = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var end = day.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var uid = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Id + "\nDATE:" + start))).ToLowerInvariant();
            if (!seen.Add(uid)) { skipped++; continue; }
            count++;
            lines.AddRange(["BEGIN:VEVENT", "UID:" + uid, "DTSTAMP:" + Utc(generatedAt), "DTSTART;VALUE=DATE:" + start,
                "DTEND;VALUE=DATE:" + end, "TRANSP:TRANSPARENT", "SUMMARY:" + Escape(entry.Summary)]);
            if (!string.IsNullOrWhiteSpace(entry.Description)) lines.Add("DESCRIPTION:" + Escape(entry.Description));
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");
        return new(string.Join("\r\n", lines.Select(Fold)) + "\r\n", count, skipped);
    }
    /// <summary>Raw values, length-prefixed in UTF-16; presentation aliases never change identity.</summary>
    public static string CanonicalId(string groupId, string subjectRaw, string? teacherRaw = null, string? classroomRaw = null)
        => string.Concat(new[] { groupId, subjectRaw, teacherRaw ?? "", classroomRaw ?? "" }
            .Select(value => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value));

    public static CalendarExportResult Create(IEnumerable<CalendarExportEntry> entries, string name, DateTimeOffset generatedAt)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Расписание военмех//Расписание//RU",
            "CALSCALE:GREGORIAN", "X-WR-CALNAME:" + Escape(name) };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0; var skipped = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Summary) || entry.End <= entry.Start)
            { skipped++; continue; }
            var uid = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Id + "\n" + Utc(entry.Start)))).ToLowerInvariant();
            if (!seen.Add(uid)) { skipped++; continue; }
            count++;
            lines.AddRange(["BEGIN:VEVENT", "UID:" + uid, "DTSTAMP:" + Utc(generatedAt), "DTSTART:" + Utc(entry.Start),
                "DTEND:" + Utc(entry.End), "SUMMARY:" + Escape(entry.Summary)]);
            if (!string.IsNullOrWhiteSpace(entry.Location)) lines.Add("LOCATION:" + Escape(entry.Location));
            if (!string.IsNullOrWhiteSpace(entry.Description)) lines.Add("DESCRIPTION:" + Escape(entry.Description));
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");
        return new(string.Join("\r\n", lines.Select(Fold)) + "\r\n", count, skipped);
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    private static string Escape(string value)
    {
        var clean = new StringBuilder();
        foreach (var c in value)
            if (c is '\t' or '\r' or '\n' || !char.IsControl(c)) clean.Append(c);
        return clean.ToString().Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal);
    }
    private static string Fold(string value)
    {
        var result = new StringBuilder(); var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString(); var length = Encoding.UTF8.GetByteCount(text);
            if (bytes + length > 75) { result.Append("\r\n "); bytes = 1; }
            result.Append(text); bytes += length;
        }
        return result.ToString();
    }
}
