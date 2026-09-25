using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Chat;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ChatInboxBrowseTests
{
    [Fact]
    public void Search_and_source_filter_preserve_order_and_count_unread()
    {
        var group = Row("О3313", "Конспект", 2);
        var direct = Row("О3313 · Аня", "Привет", 1, kind: "Личный в группе");
        var friend = Row("Борис", "Конспект", 4, personal: true);
        ChatInboxRow[] rows = [group, direct, friend];

        Assert.Equal(new[] { group, friend }, ChatInboxBrowse.Filter(rows, "  КОНСПЕКТ ", 0));
        Assert.Equal(new[] { direct }, ChatInboxBrowse.Filter(rows, "", 2));
        Assert.Equal(new[] { friend }, ChatInboxBrowse.Filter(rows, "", 3));
        Assert.Equal(7, ChatInboxBrowse.UnreadTotal(rows));
        Assert.Equal(rows, ChatInboxBrowse.Filter(rows, "", -1));
        Assert.Empty(ChatInboxBrowse.Filter(rows, "нет", 0));
        var historical = new ChatInboxRow(Guid.NewGuid(), null, true, "Старый чат", "",
            new DateTimeOffset(2020, 3, 1, 9, 0, 0, TimeSpan.Zero), 0, new RelayCommand(() => { }));
        Assert.Contains("2020", historical.When);
    }

    [Fact]
    public void Unread_badge_caps_visually_but_keeps_the_exact_accessible_count()
    {
        var row = Row("Аня", "Привет", 120, personal: true);
        Assert.Equal("99+", row.Unread);
        Assert.Equal("Непрочитанных сообщений: 120", row.UnreadDescription);
    }

    private static ChatInboxRow Row(string title, string preview, int unread, bool personal = false, string? kind = null)
        => new(Guid.NewGuid(), personal ? null : Guid.NewGuid(), personal, title, preview,
            new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.Zero), unread,
            new RelayCommand(() => { }), kind);
}
