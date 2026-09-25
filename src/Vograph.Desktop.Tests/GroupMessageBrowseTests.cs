using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class GroupMessageBrowseTests
{
    [Fact]
    public void Search_and_filters_use_loaded_body_mine_and_media_kind_without_reordering()
    {
        var mine = Message("Мой конспект", true, "text");
        var photo = Message("КОНСПЕКТ.png", false, "image");
        var file = Message("лекция.pdf", false, "file");
        var voice = Message("", true, "voice");
        var deleted = Message("секрет", false, "text", deleted: true);
        GroupMessageRow[] rows = [mine, photo, file, voice, deleted];

        Assert.Equal(new[] { mine, photo }, GroupMessageBrowse.Filter(rows, "  конспект  ", 0, 0));
        Assert.Equal(new[] { photo, file }, GroupMessageBrowse.Filter(rows, "", 2, 0));
        Assert.Equal(new[] { photo }, GroupMessageBrowse.Filter(rows, "", 2, 2));
        Assert.Equal(new[] { file }, GroupMessageBrowse.Filter(rows, "", 0, 3));
        Assert.Equal(new[] { voice }, GroupMessageBrowse.Filter(rows, "", 1, 4));
        Assert.Equal(rows, GroupMessageBrowse.Filter(rows, "", 0, 0));
        Assert.Equal(rows, GroupMessageBrowse.Filter(rows, "", -1, -1));
        Assert.Empty(GroupMessageBrowse.Filter(rows, "неизвестно", 0, 0));
    }

    [Fact]
    public void Copy_text_excludes_deleted_empty_and_media_messages()
    {
        Assert.Equal("Конспект", GroupMessageBrowse.CopyText(Message("Конспект", false, "text")));
        Assert.Null(GroupMessageBrowse.CopyText(Message("лекция.pdf", false, "file")));
        Assert.Null(GroupMessageBrowse.CopyText(Message("секрет", false, "text", deleted: true)));
        Assert.Null(GroupMessageBrowse.CopyText(Message("  ", true, "voice")));
    }

    [Fact]
    public void Next_unread_skips_aggregate_and_current_channel_and_uses_display_order()
    {
        var current = Topic(null, "Общий", 3);
        var first = Topic(Guid.NewGuid(), "Объявления", 2);
        var second = Topic(Guid.NewGuid(), "Учёба", 1);
        var aggregate = new GroupChannelRow(new GroupTopicResponse(null, "Все голосования", "🗳", null,
            null, null, 9, false, "ballots"), new RelayCommand(() => { }), true);

        Assert.Same(first, GroupBrowse.NextUnread([current, aggregate, first, second], current));
        Assert.Same(second, GroupBrowse.NextUnread([current, aggregate, first, second], first));
        Assert.Same(current, GroupBrowse.NextUnread([current, aggregate], null));
        Assert.Null(GroupBrowse.NextUnread([current, aggregate], current));
    }

    private static GroupMessageRow Message(string body, bool mine, string kind, bool deleted = false)
        => new(Guid.NewGuid(), mine ? "Я" : "Друг", body, "сейчас", mine, kind, deleted);

    private static GroupChannelRow Topic(Guid? id, string title, int unread)
        => new(new GroupTopicResponse(id, title, "💬", null, null, null, unread, false),
            new RelayCommand(() => { }));
}
