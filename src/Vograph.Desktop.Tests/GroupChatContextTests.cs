using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class GroupChatContextTests
{
    [Fact]
    public void ExactAcademicGroupMatchProtectsTheNextLesson()
    {
        var reads = 0;
        GroupLessonHint? Read()
        {
            reads++;
            return new(new DateTime(2026, 9, 25), "12:40", "Физика", "311");
        }

        Assert.Null(GroupChatContext.ForMatchingGroup("Н163С", "Н162С", Read));
        Assert.Equal(0, reads);
        Assert.Equal("Физика", GroupChatContext.ForMatchingGroup(" Н162С ", "н162с", Read)?.Subject);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void SyntheticBallotAggregateDoesNotInflateGroupActivity()
    {
        var summary = GroupChatContext.Compose(
        [
            new(null, "ballots", 50, 9),
            new(null, "chat", 3, 0),
            new(Guid.NewGuid(), "ballots", 0, 2)
        ], null);
        Assert.Equal(3, summary.Unread);
        Assert.Equal(2, summary.ActiveBallots);
        Assert.True(summary.HasContent);
        Assert.False(GroupChatContext.Compose([new(null, "ballots", 50, 9)], null).HasContent);
    }
}
