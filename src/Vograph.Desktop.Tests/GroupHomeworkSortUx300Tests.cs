using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupHomeworkSortUx300Tests
{
    [Fact]
    public void Nearest_deadline_keeps_no_deadline_last_and_does_not_change_completion()
    {
        SpaceHomeworkRow Row(string title, DateTimeOffset? due, bool done) => new(
            new GroupHomeworkCopyResponse(Guid.NewGuid(), title, "", 1, done, 0, due), true,
            _ => Task.CompletedTask, _ => { });
        var none = Row("Без срока", null, false);
        var later = Row("Позднее", new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), true);
        var sooner = Row("Раньше", new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), false);

        var result = GroupHomeworkBrowse.Sort([none, later, sooner], 1);

        Assert.Equal(["Раньше", "Позднее", "Без срока"], result.Select(row => row.Title));
        Assert.True(later.Completed);
    }
}
