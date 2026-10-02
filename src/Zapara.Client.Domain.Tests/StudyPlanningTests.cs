using Zapara.Client.Domain;
using Xunit;

namespace Zapara.Client.Domain.Tests;
public class StudyPlanningTests
{
    [Fact] public void Common_gaps_use_both_known_spans_and_merge_busy_intervals()
    {
        StudyInterval[] first = [new("9:00", "10:00"), new("12:00", "13:00")];
        var rows = StudyPlanning.CommonFreeIntervals(first, [new("09:30", "10:30"), new("11:30", "12:30")]);
        Assert.Equal(new FreeStudyInterval("10:30", "11:30", 60), Assert.Single(rows));
        Assert.Empty(StudyPlanning.CommonFreeIntervals(first, []));
        Assert.Empty(StudyPlanning.CommonFreeIntervals(first, [new("09:00", "bad")]));
        Assert.Empty(StudyPlanning.CommonFreeIntervals(first, [new("09:30", "10:30"), new("10:15", "11:45"), new("12:00", "12:30")], 20));
    }
    [Fact] public void Transfer_never_claims_unknown_or_overlapping_pairs_reachable()
    {
        Assert.Equal("tight", StudyPlanning.AssessTransfer("10:00", "10:05", 301).Status);
        Assert.Equal("fits", StudyPlanning.AssessTransfer("10:00", "10:05", 300).Status);
        Assert.Equal("unknown", StudyPlanning.AssessTransfer("10:00", "10:05", null).Status);
        Assert.Equal("overlap", StudyPlanning.AssessTransfer("10:00", "09:55", 0).Status);
        Assert.Equal("unknown", StudyPlanning.AssessTransfer("24:00", "10:00", 0).Status);
    }
    [Fact] public void All_day_deadline_keeps_date_and_exclusive_year_rollover()
    {
        var entry = new AllDayCalendarEntry("task", new(2026, 12, 31), "Задача, 1;\nстрока");
        var result = CalendarExport.CreateAllDay([entry, entry, entry with { Day = null }, entry with { Day = DateOnly.MaxValue }], "Домашка", DateTimeOffset.UtcNow);
        Assert.Equal(1, result.EventCount); Assert.Equal(3, result.SkippedCount);
        Assert.Contains("DTSTART;VALUE=DATE:20261231\r\nDTEND;VALUE=DATE:20270101", result.Content);
        Assert.Contains("SUMMARY:Задача\\, 1\\;\\nстрока", result.Content);
        Assert.Contains("TRANSP:TRANSPARENT", result.Content);
    }
}
