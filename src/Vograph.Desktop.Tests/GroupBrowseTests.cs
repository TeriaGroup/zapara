using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class GroupBrowseTests
{
    [Fact]
    public void Search_finds_group_names_and_member_handles_without_case_or_outer_spaces()
    {
        var groups = new[] {
            new GroupCommunityRow("О3313", "Участник", new RelayCommand(() => { })),
            new GroupCommunityRow("Военмех", "Участник", new RelayCommand(() => { }))
        };
        Assert.Equal("Военмех", Assert.Single(GroupBrowse.Communities(groups, "  ВОЕН  ")).Name);

        var people = new[] {
            new GroupPersonRow("Максим", "@max", "", "", "", false, null),
            new GroupPersonRow("Глеб Бова", "@gleb42", "", "", "", false, null)
        };
        Assert.Equal("Глеб Бова", Assert.Single(GroupBrowse.People(people, "GLEB42")).Name);
    }

    [Fact]
    public void Channels_prioritize_general_pinned_unread_and_combine_filters()
    {
        var rows = new[] {
            Topic(Guid.NewGuid(), "Конспекты", "chat", 0),
            Topic(Guid.NewGuid(), "Учёба", "chat", 3),
            Topic(Guid.NewGuid(), "Объявления", "chat", 0, pinned: true),
            Topic(null, "Общий поток", "chat", 0),
            Topic(Guid.NewGuid(), "Опросы", "ballots", 1, description: "После пар"),
            new GroupChannelRow(new GroupTopicResponse(null, "Все голосования", "🗳️", null, null, null,
                0, false, "ballots"), new RelayCommand(() => { }), true)
        };
        Assert.Equal(new[] { "Общий поток", "Объявления", "Учёба", "Опросы", "Конспекты", "Все голосования" },
            GroupBrowse.Channels(rows, "", "all", false).Select(row => row.Title));
        Assert.Equal("Опросы", Assert.Single(GroupBrowse.Channels(rows, " ПОСЛЕ ", "ballots", true)).Title);
        Assert.Empty(GroupBrowse.Channels(rows, "нет совпадений", "all", false));
    }

    [Fact]
    public void Chat_channel_activity_names_last_sender_and_local_event_time()
    {
        var row = new GroupChannelRow(new GroupTopicResponse(Guid.NewGuid(), "Учёба", "💬",
            "Следующая пара", "Аня", new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.Zero),
            1, false), new RelayCommand(() => { }));

        Assert.Contains("Аня", row.ActivityText);
        Assert.Contains("25.09", row.ActivityText);
        var historical = new GroupChannelRow(new GroupTopicResponse(Guid.NewGuid(), "Архив", "💬",
            "Старое", "Борис", new DateTimeOffset(2020, 3, 1, 9, 0, 0, TimeSpan.Zero),
            0, false), new RelayCommand(() => { }));
        Assert.Contains("2020", historical.ActivityText);
    }

    [Fact]
    public void Channel_and_group_direct_unread_badges_cap_without_losing_accessible_count()
    {
        var channel = Topic(Guid.NewGuid(), "Учёба", "chat", 120);
        var direct = new GroupPersonRow("Аня", "", "", "", "120", false, null);
        Assert.Equal("99+", channel.Unread);
        Assert.Equal("Непрочитанных сообщений: 120", channel.UnreadDescription);
        Assert.Equal("99+", direct.Unread);
        Assert.Equal("Непрочитанных сообщений: 120", direct.UnreadDescription);
    }

    private static GroupChannelRow Topic(Guid? id, string title, string kind, int unread,
        bool pinned = false, string description = "")
        => new(new GroupTopicResponse(id, title, "💬", null, null, null, unread, false,
            kind, description: description, pinned: pinned), new RelayCommand(() => { }));
}
