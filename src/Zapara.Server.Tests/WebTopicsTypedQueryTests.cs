using System.Text.Json;
using Xunit;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Tests;

// #33: web-клиент всегда шлёт ?typed=1 к каналам группы; web-прокси отвечал 400 на любой query-параметр.
public sealed class WebTopicsTypedQueryTests
{
    [Fact]
    public async Task Browser_topic_routes_accept_typed_query_and_still_reject_other_parameters()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await db.Accounts.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
        await using var host = new WebAccountHost(db.Accounts, moduleSettings: new()
        {
            ["Communities:Enabled"] = "true",
            ["Communities:Schema"] = db.Schema
        });
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new Zapara.Contracts.Accounts.RegisterRequest("typed_topics_browser", WebAccountHost.Password));
        var session = await host.Login("typed_topics_browser");
        var user = session.GetProperty("user").GetProperty("userId").GetGuid();
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, user);

        var list = await host.Send("GET", $"/communities/{community}/topics?typed=1", 200);
        Assert.Equal(JsonValueKind.Array, list.GetProperty("topics").ValueKind);

        var created = await host.Send("POST", $"/communities/{community}/topics?typed=1", 201, new GroupTopicRequest("Математика", "📚", "chat"));
        var topic = created.GetProperty("topics").EnumerateArray()
            .Single(item => item.TryGetProperty("topicId", out var id) && id.ValueKind == JsonValueKind.String && item.GetProperty("title").GetString() == "Математика");
        var topicId = topic.GetProperty("topicId").GetString();
        Assert.Equal("chat", topic.GetProperty("kind").GetString());

        await host.Send("POST", $"/communities/{community}/topics/{topicId}?typed=1", 200, new GroupTopicRequest("Матанализ", "📚", "chat"));
        var deleted = await host.Send("POST", $"/communities/{community}/topics/{topicId}/delete?typed=1", 200);
        Assert.DoesNotContain(deleted.GetProperty("topics").EnumerateArray(),
            item => item.TryGetProperty("topicId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == topicId);

        await host.Send("GET", $"/communities/{community}/topics", 200);
        await host.Send("GET", $"/communities/{community}/topics?typed=2", 400);
        await host.Send("GET", $"/communities/{community}/topics?typed=1&typed=1", 400);
        await host.Send("GET", $"/communities/{community}/topics?other=1", 400);
    }
}
