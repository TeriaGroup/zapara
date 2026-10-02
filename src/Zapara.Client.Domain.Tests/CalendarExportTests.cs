using System.Text;
using Zapara.Client.Domain;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public class CalendarExportTests
{
    private static readonly DateTimeOffset Generated = DateTimeOffset.Parse("2026-10-01T20:00:00Z");
    private static CalendarExportEntry Lesson(string id = "3313|2026-10-02|Математика") => new(id,
        DateTimeOffset.Parse("2026-10-02T09:00:00+03:00"), DateTimeOffset.Parse("2026-10-02T10:35:00+03:00"), "Математика", "320 УЛК");

    [Fact]
    public void Raw_occurrence_identity_is_unambiguous_and_portable()
    {
        Assert.Equal("4:331310:Математика6:Петров5:320*;", CalendarExport.CanonicalId("3313", "Математика", "Петров", "320*;"));
        Assert.Equal("1:a2:🚀0:0:", CalendarExport.CanonicalId("a", "🚀"));
        Assert.NotEqual(CalendarExport.CanonicalId("a\nb", "c"), CalendarExport.CanonicalId("a", "b\nc"));
    }

    [Fact]
    public void Uses_UTC_instants_and_keeps_generation_time_separate()
    {
        var result = CalendarExport.Create([Lesson()], "Неделя 09С52", Generated);
        Assert.Equal(1, result.EventCount); Assert.Equal(0, result.SkippedCount);
        Assert.Contains("DTSTART:20261002T060000Z\r\nDTEND:20261002T073500Z", result.Content);
        Assert.Contains("DTSTAMP:20261001T200000Z", result.Content);
        Assert.EndsWith("END:VCALENDAR\r\n", result.Content);
    }

    [Fact]
    public void Folds_UTF8_without_breaking_Russian_or_emoji_and_escapes_text()
    {
        var title = string.Concat(Enumerable.Repeat("Длинное название 🚀 ", 12));
        var result = CalendarExport.Create([Lesson() with { Summary = title, Description = "Строка\nBEGIN:VEVENT; две, ещё\\путь" }], title, Generated);
        Assert.All(result.Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), line => Assert.InRange(Encoding.UTF8.GetByteCount(line), 1, 75));
        var unfolded = result.Content.Replace("\r\n ", "");
        Assert.Contains("SUMMARY:" + title, unfolded);
        Assert.Contains("DESCRIPTION:Строка\\nBEGIN:VEVENT\\; две\\, ещё\\\\путь", unfolded);
        Assert.Equal(1, unfolded.Split("\r\n").Count(line => line == "BEGIN:VEVENT"));
        Assert.DoesNotContain("�", result.Content);
    }

    [Fact]
    public void Deduplicates_the_same_occurrence_and_reports_bad_rows()
    {
        var first = Lesson();
        var result = CalendarExport.Create([first, first with { Start = first.Start.ToUniversalTime() },
            first with { Start = first.Start.AddDays(7), End = first.End.AddDays(7) },
            first with { Id = "bad", End = first.Start.AddMinutes(-1) }, first with { Id = "empty", Summary = " " }], "Пары", Generated);
        Assert.Equal(2, result.EventCount); Assert.Equal(3, result.SkippedCount);
        var repeated = CalendarExport.Create([first], "Пары", Generated.AddDays(1));
        Assert.Equal(result.Content.Split("\r\n").First(x => x.StartsWith("UID:")), repeated.Content.Split("\r\n").First(x => x.StartsWith("UID:")));
    }
}
