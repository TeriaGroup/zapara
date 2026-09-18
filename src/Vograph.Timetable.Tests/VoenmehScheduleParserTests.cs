using Vograph.Timetable;
using Xunit;

namespace Vograph.Timetable.Tests;

public sealed class VoenmehScheduleParserTests
{
    private const string MetaJson = """
        {
          "has_data": true,
          "updated_at": "2026-09-10T09:16:14.998513Z",
          "period": "ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.",
          "groups": ["А863С", "09С33"]
        }
        """;

    private const string LessonsJson = """
        {
          "lessons": [
            {"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"ВЫСШ. МАТ.","teachers":["Барт Е.Л."],"rooms":["493"]},
            {"day":1,"time":"12:40","week":"odd","kind":"пр","subject":"ОСН.РОС.ГОС","teachers":["Лысенко Е.М."],"rooms":["563*"]},
            {"day":3,"time":"12:40","week":"odd","kind":"лаб","subject":"ФИЗИКА","teachers":[],"rooms":["323*"]},
            {"day":1,"time":"10:50","week":"even","kind":"пр","subject":"ЭК ПО ФК И СПОРТУ","teachers":[],"rooms":[]}
          ]
        }
        """;

    [Fact]
    public void Meta_reads_autumn_period_and_group_names()
    {
        var meta = VoenmehScheduleParser.ParseMeta(MetaJson);
        Assert.Equal(new DateTime(2026, 9, 1), meta.PeriodStart);
        Assert.Equal(2, meta.WeekCount);
        Assert.Equal("ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.", meta.PeriodTitle);
        Assert.Equal(new[] { "А863С", "09С33" }, meta.Groups);
    }

    [Fact]
    public void Spring_period_uses_the_second_year()
    {
        var meta = VoenmehScheduleParser.ParseMeta(
            """{"has_data":true,"period":"ВЕСЕННИЙ СЕМЕСТР 2025/2026 уч. г.","groups":["А863С"]}""");
        Assert.Equal(new DateTime(2026, 2, 9), meta.PeriodStart);
    }

    [Fact]
    public void Lessons_map_type_parity_building_and_time_end()
    {
        var lessons = VoenmehScheduleParser.ParseLessons(LessonsJson, "А863С");
        Assert.Equal(4, lessons.Count);
        var math = lessons.First(l => l.SubjectRaw.Contains("ВЫСШ"));
        Assert.Equal(1, math.DayOfWeek);
        Assert.Equal(1, math.Parity);
        Assert.Equal("09:00", math.TimeStart);
        Assert.Equal("10:35", math.TimeEnd);
        Assert.Equal("лек", math.TypeRaw);
        Assert.Equal("лек ВЫСШ. МАТ.", math.SubjectRaw);
        Assert.Equal("Барт Е.Л.", math.TeacherRaw);
        Assert.Equal("ГК", math.BuildingRaw);
        Assert.Equal("493", math.RoomRaw);
        var org = lessons.First(l => l.SubjectRaw.Contains("ОСН"));
        Assert.Equal("УЛК", org.BuildingRaw);
        Assert.Equal("563", org.RoomRaw);
        var sport = lessons.First(l => l.SubjectRaw.Contains("ФК"));
        Assert.Equal(2, sport.Parity);
        Assert.Equal("", sport.ClassroomRaw);
        Assert.Equal("", sport.BuildingRaw);
    }

    [Fact]
    public void Lessons_are_numbered_by_time_within_day_and_parity()
    {
        const string shuffled = """
            {"lessons":[
              {"day":1,"time":"10:50","week":"odd","kind":"пр","subject":"ФК","teachers":[],"rooms":[]},
              {"day":1,"time":"12:40","week":"odd","kind":"пр","subject":"ОСН","teachers":[],"rooms":["563*"]},
              {"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"МАТ.","teachers":[],"rooms":["493"]},
              {"day":3,"time":"9:00","week":"odd","kind":"лек","subject":"ИСТОРИЯ","teachers":[],"rooms":[]}
            ]}
            """;
        var lessons = VoenmehScheduleParser.ParseLessons(shuffled, "А863С");
        var monday = lessons.Where(l => l.DayOfWeek == 1 && l.Parity == 1).OrderBy(l => l.Index).ToList();
        Assert.Equal(new[] { "09:00", "10:50", "12:40" }, monday.Select(l => l.TimeStart));
        Assert.Equal(new[] { 1, 2, 3 }, monday.Select(l => l.Index));
        Assert.Equal(1, lessons.Single(l => l.DayOfWeek == 3).Index);
    }

    [Fact]
    public void Assemble_keeps_empty_groups_and_uses_name_as_id()
    {
        var parsed = VoenmehScheduleParser.Assemble(
            VoenmehScheduleParser.ParseMeta(MetaJson),
            new[] { ("А863С", VoenmehScheduleParser.ParseLessons(LessonsJson, "А863С")) });
        Assert.Equal(new HashSet<string> { "А863С", "09С33" }, parsed.Groups.Select(g => g.Id).ToHashSet());
        Assert.All(parsed.Lessons, l => Assert.Equal("А863С", l.GroupId));
        Assert.Equal(VoenmehScheduleClient.Origin, parsed.Groups[0].Url);
        Assert.Equal(new[] { "А863С" }, parsed.FetchedGroupNames);
    }

    [Fact]
    public void Html_is_not_a_schedule_payload()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            VoenmehScheduleParser.ParseMeta("<!doctype html><html></html>"));
        Assert.Equal(TimetableParser.NotTimetable, ex.Message);
        var hummingbird = Assert.Throws<InvalidOperationException>(() =>
            VoenmehScheduleParser.ParseMeta("<!-- This page is cached --><!DOCTYPE html><html></html>"));
        Assert.Equal(TimetableParser.NotTimetable, hummingbird.Message);
    }

    [Fact]
    public async Task Client_fetches_only_named_groups()
    {
        var http = new List<string>();
        var client = new VoenmehScheduleClient((url, _) =>
        {
            http.Add(url);
            if (url.EndsWith("/meta", StringComparison.Ordinal))
                return Task.FromResult(MetaJson);
            if (url.Contains(Uri.EscapeDataString("А863С"), StringComparison.Ordinal))
                return Task.FromResult(LessonsJson);
            throw new InvalidOperationException(url);
        });
        var parsed = await client.FetchAsync(new[] { "А863С" });
        Assert.Equal(2, parsed.Groups.Count);
        Assert.Equal(4, parsed.Lessons.Count);
        Assert.Equal(1, http.Count(u => u.Contains("/api/schedule/lessons", StringComparison.Ordinal)));
        Assert.DoesNotContain(http, u => u.Contains(Uri.EscapeDataString("09С33")));
    }

    [Fact]
    public async Task Client_catalog_only_when_no_group_names()
    {
        var http = new List<string>();
        var client = new VoenmehScheduleClient((url, _) =>
        {
            http.Add(url);
            return Task.FromResult(MetaJson);
        });
        var parsed = await client.FetchAsync(Array.Empty<string>());
        Assert.Equal(2, parsed.Groups.Count);
        Assert.Empty(parsed.Lessons);
        Assert.DoesNotContain(http, u => u.Contains("/api/schedule/lessons", StringComparison.Ordinal));
        Assert.Empty(parsed.FetchedGroupNames!);
    }

    [Fact]
    public async Task Client_html_on_lessons_fails_the_refresh()
    {
        var client = new VoenmehScheduleClient((url, _) =>
            Task.FromResult(url.EndsWith("/meta", StringComparison.Ordinal) ? MetaJson : "<!doctype html><html></html>"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchAsync(new[] { "А863С" }));
        Assert.Equal(TimetableParser.NotTimetable, ex.Message);
    }
}
