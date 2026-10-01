using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class GroupMessageDraftValidationTests
{
    [Fact]
    public void Context_and_separator_count_toward_the_same_unicode_scalar_limit()
    {
        Assert.True(GroupMessageDraftValidation.Check(new string('я', 1997), "A").IsValid);
        var tooLong = GroupMessageDraftValidation.Check(new string('я', 1998), "A");
        Assert.False(tooLong.IsValid);
        Assert.Equal(2001, tooLong.Count);
        Assert.True(GroupMessageDraftValidation.Check("🙂" + new string('a', 1999), "").IsValid);
        Assert.Equal(2000, GroupMessageDraftValidation.Check("🙂" + new string('a', 1999), "").Count);
    }

    [Fact]
    public void Multiline_is_preserved_but_broken_utf16_and_controls_are_rejected()
    {
        Assert.True(GroupMessageDraftValidation.Check("Первая\r\nВторая\nТретья", "").IsValid);
        Assert.False(GroupMessageDraftValidation.Check("Текст\uD800", "").IsValid);
        Assert.False(GroupMessageDraftValidation.Check("Текст\u0001", "").IsValid);
        Assert.True(GroupMessageDraftValidation.Check("Текст\tпосле", "").IsValid);
    }
}
