using System.Text.RegularExpressions;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>G-3: одно понятие — одно название (глоссарий ревью раунда 2), строки берутся из общего каталога design/strings.</summary>
public class GlossaryTests
{
    private static readonly I18nService I18n = new();

    [Theory]
    [InlineData("noLessons", "Пар нет")]
    [InlineData("noLessonsShort", "Пар нет")]
    [InlineData("noLessonsDay", "Пар нет")]
    [InlineData("emptyWeek", "Пар нет")]
    [InlineData("breaksTitle", "Перерывы и окна")]
    [InlineData("hwMarkDone", "Выполнено")]
    [InlineData("hwGroupDone", "Выполнено")]
    [InlineData("hwDone", "выполнено")]
    [InlineData("groupChat", "Чат группы")]
    [InlineData("navFriends", "Друзья")]
    public void Glossary_terms_come_from_the_shared_catalog(string key, string expected) => Assert.Equal(expected, I18n.T(key));

    [Fact]
    public void Formats_follow_the_glossary()
    {
        Assert.Equal("открыто 2 · выполнено 0", I18n.T("hwOpenDone", 2, 0));
        Assert.Equal("пн, 12 окт.", LessonText.ShortDate(new DateTime(2026, 10, 12)));
        Assert.Equal("Следующая пара: пн, 12 окт., 10:50", I18n.T("nextLessonHint", LessonText.ShortDate(new DateTime(2026, 10, 12)), "10:50"));
        Assert.Equal("268 (Фесто)", LessonText.CleanRoom("268(фесто)"));
    }

    [Fact]
    public void Forbidden_variants_are_gone_from_strings_and_views()
    {
        var forbidden = new Regex("Нет занятий|нет занятий»|\\bСдано\\b|\\bсдано\\b|Окна между парами|Следующий учебный день|Чатик");
        var values = typeof(I18nService).GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(I18n) as IDictionary<string, string>;
        Assert.NotNull(values);
        Assert.DoesNotContain(values!, pair => forbidden.IsMatch(pair.Value) && pair.Key != "hwNoDate");
        var desktop = Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop");
        var hits = Directory.EnumerateFiles(desktop, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("<!--") && forbidden.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.Empty(hits);
    }
}
