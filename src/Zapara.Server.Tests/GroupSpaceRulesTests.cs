using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
namespace Zapara.Server.Tests;

public sealed class GroupSpaceRulesTests
{
    [Fact]
    public void Larger_response_collections_do_not_relax_request_size_validation()
    {
        var bytes = CommunityJson.Serialize(new string('x', CommunityValidation.RequestBytes));
        Assert.Throws<ArgumentException>(() => CommunityJson.Parse<string>(bytes));
        Assert.Equal(CommunityValidation.RequestBytes, CommunityJson.ParseResponse<string>(bytes).Length);
    }
    [Fact]
    public void Role_allow_is_applied_after_all_role_denies()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var p = GroupPermissionRules.Calculate(true, false, false, [], [a, b], [new(a, "read", "deny"), new(b, "read", "allow")], "chat", "chat", false, "all");
        Assert.Contains("read", p); Assert.Contains("post", p);
    }
    [Fact]
    public void Missing_read_removes_content_even_for_channel_manager()
    {
        var p = GroupPermissionRules.Calculate(true, false, false, ["channels"], [], [new(null, "read", "deny")], "chat", "chat", false, "all");
        Assert.DoesNotContain("read", p); Assert.DoesNotContain("post", p);
    }
    [Fact]
    public void Read_only_can_vote_but_cannot_publish_media_or_polls()
    {
        var p = GroupPermissionRules.Calculate(true, false, false, ["ballots"], [], [new(null, "post", "deny")], "ballots", "polls", false, "all");
        Assert.Contains("vote", p); Assert.DoesNotContain("media", p); Assert.DoesNotContain("ballots", p);
    }
    [Theory]
    [InlineData("schedule", "schedule")]
    [InlineData("unknown", "unknown")]
    public void Non_chat_formats_never_allow_post(string kind, string template)
        => Assert.DoesNotContain("post", GroupPermissionRules.Calculate(true, true, false, [], [], [], kind, template, false, "all"));
    [Fact]
    public void Non_members_have_no_rights_even_with_headman_flag()
        => Assert.Empty(GroupPermissionRules.Calculate(false, true, false, [], [], [], "chat", "chat", false, "all"));
    [Fact]
    public void Delegation_cannot_reach_peer_or_new_powers()
    {
        Assert.False(GroupPermissionRules.CanDelegate(false, 2, 2, ["roles"], ["roles"]));
        Assert.False(GroupPermissionRules.CanDelegate(false, 3, 2, ["roles"], ["roles", "grants"]));
        Assert.True(GroupPermissionRules.CanDelegate(false, 3, 2, ["roles", "grants"], ["grants"]));
    }
    [Fact]
    public void Unknown_topic_kind_is_not_silently_chat()
    {
        var topic = new GroupTopicResponse(Guid.NewGuid(), "Future", "?", null, null, null, 0, false, kind: "future");
        Assert.Equal("future", topic.Kind); Assert.False(topic.CanPost); Assert.False(topic.Supported);
    }
    [Fact]
    public void Form_answers_reject_other_questions_and_missing_required()
    {
        var id = Guid.NewGuid(); var q = new GroupFormQuestion(id, "Выберите", "singleChoice", true, ["Да", "Нет"]);
        Assert.Throws<ArgumentException>(() => GroupFormRules.ValidateAnswers([q], []));
        Assert.Throws<ArgumentException>(() => GroupFormRules.ValidateAnswers([q], [new(id, null, ["Иное"])]));
        GroupFormRules.ValidateAnswers([q], [new(id, null, ["Да"])]);
    }
    [Fact]
    public void Legacy_requests_omit_unrequested_extension_fields()
    {
        using var topic = System.Text.Json.JsonDocument.Parse(CommunityJson.Serialize(new GroupTopicRequest("Канал", "chat")));
        foreach (var name in new[] { "template", "categoryId", "position", "subject", "expectedRevision" }) Assert.False(topic.RootElement.TryGetProperty(name, out _));
        using var homework = System.Text.Json.JsonDocument.Parse(CommunityJson.Serialize(new HomeworkUpsert("Задача", "Текст", 0)));
        Assert.False(homework.RootElement.TryGetProperty("deadlineAt", out _)); Assert.False(homework.RootElement.TryGetProperty("topicId", out _));
        var modern = CommunityJson.Parse<HomeworkUpsert>(CommunityJson.Serialize(new HomeworkUpsert("Задача", "Текст", 0, topicId: Guid.NewGuid())));
        Assert.NotNull(modern.TopicId);
    }
}
