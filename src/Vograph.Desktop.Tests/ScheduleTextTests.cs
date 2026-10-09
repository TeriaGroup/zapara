using System.Text.Json;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#12: те же примеры из фида, что и в web/src/schedule-text.test.ts (design/strings/schedule-cases.json).</summary>
public class ScheduleTextTests
{
    private static JsonElement Cases()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "design", "strings", "schedule-cases.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "design", "strings", "schedule-cases.json"))).RootElement;
    }

    [Fact]
    public void Subjects_rooms_teachers_and_meta_match_the_shared_cases()
    {
        var cases = Cases();
        foreach (var c in cases.GetProperty("subjects").EnumerateArray())
        {
            var row = c.EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal((row[2], row[3]), ScheduleText.Subject(row[0], row[1]));
        }
        foreach (var c in cases.GetProperty("rooms").EnumerateArray())
            Assert.Equal(c[1].GetString(), ScheduleText.Room(c[0].GetString()));
        foreach (var c in cases.GetProperty("teachers").EnumerateArray())
            Assert.Equal(c[1].GetString(), ScheduleText.Teacher(c[0].GetString()));
        foreach (var c in cases.GetProperty("meta").EnumerateArray())
            Assert.Equal(c[1].GetString(), ScheduleText.MetaLine(c[0].EnumerateArray().Select(x => x.GetString()).ToArray()));
    }

    [Fact]
    public void Desktop_display_helpers_use_the_shared_normalization()
    {
        Assert.Equal("Управление проектами", LessonText.StripType("пр УПР.ПРОЕКТАМИ", "пр"));
        Assert.Equal("Матан", LessonText.StripType("Матан", "пр"));
        Assert.Equal("268 (Фесто)", LessonText.CleanRoom("268*(фесто);"));
        Assert.Equal("Кондратьев С. А.", LessonText.Teacher("Кондратьев Сергей А."));
        Assert.Equal("Практика", ScheduleText.TypeLabel("пр"));
    }

    [Fact]
    public void I18n_takes_x01_terms_from_the_catalog()
    {
        var i18n = new I18nService("ru");
        foreach (var (key, value) in SharedStrings.Desktop) Assert.Equal(value, i18n.T(key));
        Assert.Equal("нечётная", i18n.T("odd"));
        Assert.Equal("Добавить задание", i18n.T("hwAddShort"));
        Assert.Equal("Практика", i18n.T("typePr"));
        Assert.Equal("Обсудить в чате группы", i18n.T("discussInGroupChat"));
    }

    [Fact]
    public void Dates_follow_the_glossary()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        Assert.Equal("9 окт., 18:31", ScheduleText.DateTimeText(new DateTime(2026, 10, 9, 18, 31, 0), now));
        Assert.Equal("5–11 окт.", ScheduleText.Range(new DateTime(2026, 10, 5), new DateTime(2026, 10, 11), now));
        Assert.Equal("28 сент.–4 окт.", ScheduleText.Range(new DateTime(2026, 9, 28), new DateTime(2026, 10, 4), now));
    }
}
