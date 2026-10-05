using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class HomeworkAudienceApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Negotiated_http_reports_permissions_and_preserves_previous_native_shapes()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "hw.wire.head"); var member = await Seed(host.Accounts, "hw.wire.member");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId);
        var item = await service.ShareHomeworkAsync(head.AccessToken, group, new("Предмет", "Текст", 0, audience: new("selected", [], [member.User.UserId])), Ct);
        host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
        using (var old = JsonDocument.Parse(await host.Send("GET", $"/{group}/homework/{item.HomeworkId}", 200, head.AccessToken)))
        {
            Assert.False(old.RootElement.TryGetProperty("audience", out _));
            Assert.False(old.RootElement.TryGetProperty("canComplete", out _));
            Assert.True(old.RootElement.TryGetProperty("topicId", out _));
        }
        using (var desk = JsonDocument.Parse(await host.Send("GET", $"/{group}/desk", 200, head.AccessToken)))
            Assert.False(desk.RootElement.GetProperty("capabilities").TryGetProperty("homeworkAudience", out _));
        host.Client.DefaultRequestHeaders.Add("X-Zapara-Homework", "1");
        var modern = await host.Get<HomeworkResponse>($"/{group}/homework/{item.HomeworkId}", head.AccessToken);
        Assert.Equal("selected", modern.Audience.Kind); Assert.True(modern.CanEdit); Assert.False(modern.CanComplete);
        Assert.True((await host.Get<GroupDeskResponse>($"/{group}/desk", head.AccessToken)).Capabilities.HomeworkAudience);
        var recipient = Assert.Single(await host.Get<GroupHomeworkCopyResponse[]>($"/{group}/homework/copies", member.AccessToken));
        Assert.True(recipient.CanComplete); Assert.False(recipient.CanEdit);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpdateHomeworkAsync(member.AccessToken, group, item.HomeworkId, new("Предмет", "Подмена", 1), Ct))).Status);
    }

    [Fact]
    public async Task Role_grants_are_dynamic_and_author_visibility_does_not_grant_edit_or_completion()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "hw.dynamic.head"); var author = await Seed(host.Accounts, "hw.dynamic.author"); var recipient = await Seed(host.Accounts, "hw.dynamic.recipient");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId);
        await db.SeedMemberAsync(group, author.User.UserId); await db.SeedMemberAsync(group, recipient.User.UserId);
        var role = Assert.Single((await service.CreateRoleAsync(head.AccessToken, group, new("Подгруппа"), Ct)).Roles).RoleId;
        await service.GrantRoleAsync(head.AccessToken, group, role, new(recipient.User.UserId), Ct);
        var item = await service.ShareHomeworkAsync(author.AccessToken, group, new("Задача", "Текст", 0, audience: new("selected", [role], [])), Ct);
        Assert.False(item.CanEdit); Assert.False(item.CanComplete);
        Assert.Single(await service.ListHomeworkCopiesAsync(author.AccessToken, group, Ct));
        Assert.Single(await service.ListHomeworkCopiesAsync(recipient.AccessToken, group, Ct));
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpdateHomeworkAsync(author.AccessToken, group, item.HomeworkId, new("Задача", "Новое", 1), Ct))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpsertCompletionAsync(author.AccessToken, group, item.HomeworkId, new(true, 0), Ct))).Status);
        await service.UpsertCompletionAsync(recipient.AccessToken, group, item.HomeworkId, new(true, 0), Ct);
        await service.RevokeRoleAsync(head.AccessToken, group, role, recipient.User.UserId, Ct);
        Assert.Empty(await service.ListHomeworkCopiesAsync(recipient.AccessToken, group, Ct));
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.GetCompletionAsync(recipient.AccessToken, group, item.HomeworkId, Ct))).Status);
        await service.GrantRoleAsync(head.AccessToken, group, role, new(recipient.User.UserId), Ct);
        Assert.True((await service.GetCompletionAsync(recipient.AccessToken, group, item.HomeworkId, Ct)).Completed);
    }

    [Fact]
    public async Task Selected_role_and_people_form_a_union_and_nonrecipients_cannot_read_or_complete()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "hw.audience.head");
        var member = await Seed(host.Accounts, "hw.audience.member");
        var person = await Seed(host.Accounts, "hw.audience.person");
        var other = await Seed(host.Accounts, "hw.audience.other");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId);
        foreach (var user in new[] { member, person, other }) await db.SeedMemberAsync(group, user.User.UserId);
        var role = Assert.Single((await service.CreateRoleAsync(head.AccessToken, group, new("Подгруппа 1"), Ct)).Roles).RoleId;
        await service.GrantRoleAsync(head.AccessToken, group, role, new(member.User.UserId), Ct);
        var selected = new HomeworkAudience("selected", [role, role], [member.User.UserId, person.User.UserId]);
        var item = await service.ShareHomeworkAsync(head.AccessToken, group, new("Математика", "Первое\nВторое", 0, audience: selected), Ct);
        Assert.True(item.CanEdit); Assert.False(item.CanComplete);
        Assert.Equal("Первое\nВторое", item.Body);
        Assert.Single(await service.ListHomeworkCopiesAsync(member.AccessToken, group, Ct));
        Assert.Single(await service.ListHomeworkAsync(person.AccessToken, group, Ct));
        Assert.Empty(await service.ListHomeworkCopiesAsync(other.AccessToken, group, Ct));
        Assert.Empty(await service.ListHomeworkAsync(other.AccessToken, group, Ct));
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.GetHomeworkAsync(other.AccessToken, group, item.HomeworkId, Ct))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.GetCompletionAsync(other.AccessToken, group, item.HomeworkId, Ct))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpsertCompletionAsync(other.AccessToken, group, item.HomeworkId, new(true, 0), Ct))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpsertCompletionAsync(head.AccessToken, group, item.HomeworkId, new(true, 0), Ct))).Status);
        var completed = await service.UpsertCompletionAsync(member.AccessToken, group, item.HomeworkId, new(true, 0), Ct);
        Assert.Equal(1, completed.Revision);
        Assert.False((await service.GetCompletionAsync(person.AccessToken, group, item.HomeworkId, Ct)).Completed);
        Assert.Equal(409, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpsertCompletionAsync(member.AccessToken, group, item.HomeworkId, new(false, 0), Ct))).Status);
        // An older editor does not send audience. Content editing must not widen it or reset completion.
        var edited = await service.UpdateHomeworkAsync(head.AccessToken, group, item.HomeworkId, new("Математика", "Исправлено", item.Revision), Ct);
        Assert.Equal("selected", edited.Audience.Kind);
        Assert.Empty(await service.ListHomeworkAsync(other.AccessToken, group, Ct));
        Assert.True((await service.GetCompletionAsync(member.AccessToken, group, item.HomeworkId, Ct)).Completed);
        await service.RevokeRoleAsync(head.AccessToken, group, role, member.User.UserId, Ct);
        // Explicit user selection still grants access after removal of the overlapping role.
        Assert.Single(await service.ListHomeworkAsync(member.AccessToken, group, Ct));
    }

    [Fact]
    public async Task Creation_retry_is_scoped_to_author_and_rejects_changed_payload_without_duplicates()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "hw.retry.head"); var member = await Seed(host.Accounts, "hw.retry.member");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId);
        var operation = Guid.NewGuid();
        var request = new HomeworkUpsert("Предмет", "1. Один\n2. Два", 0, operationId: operation);
        var first = await service.ShareHomeworkAsync(head.AccessToken, group, request, Ct);
        var retries = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => service.ShareHomeworkAsync(head.AccessToken, group, request, Ct)));
        Assert.All(retries, retry => Assert.Equal(first.HomeworkId, retry.HomeworkId));
        Assert.Single(await service.ListHomeworkAsync(head.AccessToken, group, Ct));
        Assert.Equal(409, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ShareHomeworkAsync(head.AccessToken, group, new("Предмет", "Другое", 0, operationId: operation), Ct))).Status);
        Assert.NotEqual(first.HomeworkId, (await service.ShareHomeworkAsync(member.AccessToken, group, request, Ct)).HomeworkId);
        var secondGroup = Guid.NewGuid(); await db.SeedCommunityAsync(secondGroup); await db.SeedStaffAsync(secondGroup, head.User.UserId);
        Assert.NotEqual(first.HomeworkId, (await service.ShareHomeworkAsync(head.AccessToken, secondGroup, request, Ct)).HomeworkId);
        Assert.Equal(409, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ShareHomeworkAsync(head.AccessToken, group,
            new("Предмет", request.Body, 0, audience: new("selected", [], [member.User.UserId]), operationId: operation), Ct))).Status);
        await service.UpdateHomeworkAsync(head.AccessToken, group, first.HomeworkId, new("Предмет", "Исправлено", 1), Ct);
        var retriedAfterEdit = await service.ShareHomeworkAsync(head.AccessToken, group, request, Ct);
        Assert.Equal(first.HomeworkId, retriedAfterEdit.HomeworkId); Assert.Equal("Исправлено", retriedAfterEdit.Body); Assert.Equal(2, retriedAfterEdit.Revision);
        for (var index = 0; index < 40; index++) await service.ShareHomeworkAsync(head.AccessToken, group, new("Предмет", "Ещё задание", 0), Ct);
        Assert.Equal(42, (await service.ListHomeworkAsync(head.AccessToken, group, Ct)).Count);
    }

    [Fact]
    public async Task Invalid_targets_and_hidden_topics_do_not_grant_access_but_archived_private_completion_is_allowed()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "hw.topic.head"); var member = await Seed(host.Accounts, "hw.topic.member"); var outsider = await Seed(host.Accounts, "hw.topic.outsider");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId);
        var foreignGroup = Guid.NewGuid(); await db.SeedCommunityAsync(foreignGroup); await db.SeedStaffAsync(foreignGroup, head.User.UserId);
        var foreignRole = Assert.Single((await service.CreateRoleAsync(head.AccessToken, foreignGroup, new("Чужая подгруппа"), Ct)).Roles).RoleId;
        foreach (var audience in new[] { new HomeworkAudience("selected", [Guid.NewGuid()], []), new HomeworkAudience("selected", [foreignRole], []), new HomeworkAudience("selected", [], [outsider.User.UserId]) })
            Assert.Equal(400, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ShareHomeworkAsync(head.AccessToken, group, new("Задача", "Текст", 0, audience: audience), Ct))).Status);
        var topic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Задания", "book", "homework"), Ct, true)).Topics, t => t.Kind == "homework");
        var id = topic.TopicId!.Value;
        var item = await service.ShareHomeworkAsync(head.AccessToken, group, new("Задача", "Текст", 0, topicId: id, audience: new("selected", [], [member.User.UserId])), Ct);
        await service.ArchiveTopicAsync(head.AccessToken, group, id, new(true, topic.Revision), Ct);
        Assert.True((await service.GetHomeworkAsync(member.AccessToken, group, item.HomeworkId, Ct)).CanComplete);
        Assert.True((await service.UpsertCompletionAsync(member.AccessToken, group, item.HomeworkId, new(true, 0), Ct)).Completed);
        await service.SetTopicAccessAsync(head.AccessToken, group, id, new([new(null, "read", "deny")], topic.Revision + 1), Ct);
        Assert.Empty(await service.ListHomeworkAsync(member.AccessToken, group, Ct));
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.GetHomeworkAsync(member.AccessToken, group, item.HomeworkId, Ct))).Status);
    }
}
