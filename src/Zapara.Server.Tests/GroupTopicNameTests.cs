using Zapara.Server.Communities;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class GroupTopicNameTests
{
    [Fact]
    public void Topic_titles_are_short_and_leave_the_general_thread_alone()
    {
        Assert.Equal("Вышмат", GroupTopicNames.Title("  Вышмат  "));
        Assert.Equal("ОРГ", GroupTopicNames.Title("ОРГ"));
        Assert.Null(GroupTopicNames.Title("Я"));
        Assert.Null(GroupTopicNames.Title("общий"));
        Assert.Null(GroupTopicNames.Title("Общий"));
        Assert.Equal("💬", GroupTopicNames.Icon(""));
        Assert.Equal("📚", GroupTopicNames.Icon("📚"));
        Assert.Null(GroupTopicNames.Icon("слишком длинная подпись"));
    }
}
