using System.Text.Json;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class GroupChannelContractsTests
{
    [Theory]
    [InlineData(null, "chat")]
    [InlineData("", "chat")]
    [InlineData("chat", "chat")]
    [InlineData("ballots", "ballots")]
    public void Channel_kind_is_canonical_and_legacy_topics_remain_chats(string? input, string expected)
        => Assert.Equal(expected, GroupTopicNames.Kind(input));

    [Theory]
    [InlineData("news")]
    [InlineData("Ballots")]
    [InlineData("chat\n")]
    public void Unknown_channel_kind_is_rejected(string input)
        => Assert.Null(GroupTopicNames.Kind(input));

    [Fact]
    public void Existing_topic_request_defaults_to_chat_and_ballot_request_preserves_its_channel()
    {
        var oldTopic = JsonSerializer.Deserialize<GroupTopicRequest>("{\"title\":\"Математика\",\"icon\":\"📚\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("chat", oldTopic?.Kind);

        var pollId = Guid.NewGuid();
        var ballot = new BallotDraftRequest("Когда встречаемся?", ["Пятница", "Суббота"], 2, pollId);
        Assert.Equal(pollId, ballot.TopicId);
    }

    [Fact]
    public void Channel_management_is_a_grantable_group_power()
    {
        Assert.True(GroupChanges.KnownPower("channels"));
        Assert.Equal("Управлять каналами", GroupChanges.PowerTitle("channels"));
    }
    [Fact]
    public void Channel_customization_is_bounded_and_older_requests_leave_new_fields_omitted()
    {
        Assert.Equal("", GroupTopicNames.Description(null));
        Assert.Equal("Вопросы по физике", GroupTopicNames.Description("  Вопросы по физике  "));
        Assert.Equal("Первая\nВторая", GroupTopicNames.Description("Первая\r\nВторая"));
        Assert.Null(GroupTopicNames.Description(new string('x', 241)));
        Assert.Equal("default", GroupTopicNames.Accent(null));
        Assert.Equal("purple", GroupTopicNames.Accent("purple"));
        Assert.Null(GroupTopicNames.Accent("url(evil)"));
        Assert.Equal("all", GroupTopicNames.WritePolicy(null));
        Assert.Equal("managers", GroupTopicNames.WritePolicy("managers"));
        Assert.Null(GroupTopicNames.WritePolicy("anyone"));

        var old = JsonSerializer.Deserialize<GroupTopicRequest>("{\"title\":\"Чат\",\"icon\":\"💬\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Null(old?.Description);
        Assert.Null(old?.Accent);
        Assert.Null(old?.Pinned);
        Assert.Null(old?.WritePolicy);

        var customized = new GroupTopicRequest("Физика", "🧪", "chat", "Вопросы по физике", "purple", true, "managers");
        Assert.Equal("Вопросы по физике", customized.Description);
        Assert.Equal("purple", customized.Accent);
        Assert.True(customized.Pinned);
        Assert.Equal("managers", customized.WritePolicy);
    }

}
