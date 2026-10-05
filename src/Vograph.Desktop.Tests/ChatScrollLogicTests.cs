using Vograph.Desktop.Features.Chat;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ChatScrollLogicTests
{
    [Fact]
    public void Appended_messages_follow_only_when_reader_is_near_latest()
    {
        Assert.True(ChatScrollLogic.NearLatest(1000, 300, 680));
        Assert.True(ChatScrollLogic.NearLatest(1000, 300, 700));
        Assert.False(ChatScrollLogic.NearLatest(1000, 300, 300));
        Assert.True(ChatScrollLogic.NearLatest(200, 300, 0));
    }

    [Fact]
    public void Prepending_an_older_page_keeps_the_same_visible_content()
    {
        Assert.Equal(460, ChatScrollLogic.AfterPrepend(300, 1000, 1160));
        Assert.Equal(300, ChatScrollLogic.AfterPrepend(300, 1000, 900));
    }
}
