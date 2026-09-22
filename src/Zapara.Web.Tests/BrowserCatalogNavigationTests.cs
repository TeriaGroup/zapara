using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserCatalogNavigationTests
{
    [Fact]
    public void Route_return_keeps_all_three_sections_but_another_owner_never_inherits_them()
    {
        var state = new BrowserCatalogViewState();
        var a = state.Activate("server:A", 1, "2947");
        var week = new WeekView(new(2026, 10, 12), 2, 3, "лек", "Математика");
        var summary = new SummaryView(1, 4, "пр", "Барт");
        var teachers = new TeacherView("Барт", true, "2449", 2, 160);
        Assert.True(state.Save(a, week)); Assert.True(state.Save(a, summary)); Assert.True(state.Save(a, teachers));
        var returning = state.Activate("server:A", 1, "2947");
        Assert.Equal(week, state.Week(returning, DateTime.Today));
        Assert.Equal(summary, state.Summary(returning)); Assert.Equal(teachers, state.Teachers(returning));
        var b = state.Activate("server:B", 2, "2947");
        Assert.Equal(new SummaryView(), state.Summary(b)); Assert.Equal(new TeacherView(), state.Teachers(b));
        Assert.False(state.Save(a, new TeacherView("late A")));
        var aAgain = state.Activate("server:A", 3, "2947");
        Assert.Equal(teachers, state.Teachers(aAgain));
        Assert.False(state.Save(a, new SummaryView(Query: "old generation")));
    }

    [Theory]
    [InlineData("day", "Понедельник", "week?day=1")]
    [InlineData("type", "лек", "week?kind=%D0%BB%D0%B5%D0%BA")]
    [InlineData("type", "лекция", "week?kind=%D0%BB%D0%B5%D0%BA")]
    [InlineData("subject", "A&B", "week?subject=A%26B")]
    [InlineData("teacher", "Барт Е.Л.", "teachers?query=%D0%91%D0%B0%D1%80%D1%82%20%D0%95.%D0%9B.")]
    [InlineData("room", "320*", "maps?room=320%2A")]
    public void Summary_items_link_to_the_related_data_without_unescaped_query_injection(string section, string name, string expected) =>
        Assert.Equal(expected, BrowserCatalogNavigation.SummaryTarget(section, name));
}
