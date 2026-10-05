using System.Text;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Tests;

public sealed class HomeworkAudienceContractTests
{
    [Fact]
    public void Legacy_response_keeps_personal_completion_and_does_not_invent_edit_or_targeting_rights()
    {
        var copy = CommunityJson.Parse<GroupHomeworkCopyResponse>(Encoding.UTF8.GetBytes("""
            {"homeworkId":"00000000-0000-0000-0000-000000000001","title":"Предмет","body":"Текст","revision":1,"completed":false,"completionRevision":0}
            """));
        Assert.True(copy.CanComplete); Assert.False(copy.CanEdit); Assert.Equal("all", copy.Audience.Kind);
        Assert.False(CommunityJson.Parse<GroupCapabilitiesResponse>(Encoding.UTF8.GetBytes("{}" )).HomeworkAudience);
        var request = new HomeworkUpsert("Предмет", "Текст", 1);
        using var json = JsonDocument.Parse(CommunityJson.Serialize(request));
        Assert.False(json.RootElement.TryGetProperty("audience", out _));
        Assert.False(json.RootElement.TryGetProperty("operationId", out _));
    }

    [Fact]
    public void Targeted_request_roundtrips_without_widening_and_deduplicates_recipients()
    {
        var request = CommunityJson.Parse<HomeworkUpsert>(Encoding.UTF8.GetBytes("""
            {"title":"Математика","body":"1. Первое\n2. Второе","expectedRevision":0,
             "audience":{"kind":"selected","roleIds":[],"userIds":["00000000-0000-0000-0000-000000000001","00000000-0000-0000-0000-000000000001"]},
             "operationId":"00000000-0000-0000-0000-000000000002"}
            """));
        using var json = JsonDocument.Parse(CommunityJson.Serialize(request));
        Assert.Equal("selected", json.RootElement.GetProperty("audience").GetProperty("kind").GetString());
        Assert.Single(json.RootElement.GetProperty("audience").GetProperty("userIds").EnumerateArray());
        Assert.Equal("1. Первое\n2. Второе", request.Body);
        Assert.Equal("00000000-0000-0000-0000-000000000002", json.RootElement.GetProperty("operationId").GetString());
    }

    [Theory]
    [InlineData("{\"kind\":\"selected\",\"roleIds\":[],\"userIds\":[]}")]
    [InlineData("{\"kind\":\"all\",\"roleIds\":[\"00000000-0000-0000-0000-000000000001\"],\"userIds\":[]}")]
    [InlineData("{\"kind\":\"unknown\",\"roleIds\":[],\"userIds\":[]}")]
    [InlineData("{\"kind\":\"selected\",\"roleIds\":[],\"userIds\":[\"00000000-0000-0000-0000-000000000000\"]}")]
    public void Invalid_audience_is_rejected_instead_of_falling_back_to_everyone(string audience)
        => Assert.Throws<ArgumentException>(() => CommunityJson.Parse<HomeworkUpsert>(Encoding.UTF8.GetBytes(
            "{\"title\":\"Задача\",\"body\":\"Текст\",\"expectedRevision\":0,\"audience\":" + audience + "}")));
}
