using Microsoft.Extensions.DependencyInjection;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;
namespace Zapara.Server.Tests;

public sealed class GroupSpaceApiTests
{
    [Fact]
    public async Task Delegated_pin_and_moderation_do_not_grant_channel_editing_or_access_escalation()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>(); var ct = TestContext.Current.CancellationToken;
        var head = await Seed(host.Accounts, "space.moderation.head"); var moderator = await Seed(host.Accounts, "space.moderation.member");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, moderator.User.UserId);
        var role = Assert.Single((await service.CreateRoleAsync(head.AccessToken, group, new("Модератор"), ct)).Roles).RoleId;
        foreach (var power in new[] { "pin", "moderate", "access" }) await service.SetRolePowerAsync(head.AccessToken, group, role, new(power, true), ct);
        await service.GrantRoleAsync(head.AccessToken, group, role, new(moderator.User.UserId), ct);
        var topic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Обсуждение", "chat"), ct, true)).Topics, t => t.TopicId is not null);
        var id = topic.TopicId!.Value;
        var pinned = (await service.RenameTopicAsync(moderator.AccessToken, group, id, new(topic.Title, topic.Icon, pinned: true, expectedRevision: topic.Revision), ct, true)).Topics.Single(t => t.TopicId == id);
        Assert.True(pinned.Pinned);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.RenameTopicAsync(moderator.AccessToken, group, id, new("Чужое имя", topic.Icon), ct))).Status);
        var chat = (await service.GroupHomeAsync(head.AccessToken, group, ct)).GroupChat.ConversationId;
        var message = await service.SendTopicMessageAsync(head.AccessToken, chat, new("Сообщение", id), ct);
        Assert.True((await service.DeleteMessageAsync(moderator.AccessToken, chat, message.MessageId, ct)).Deleted);
        await service.SetTopicAccessAsync(head.AccessToken, group, id, new([new(null, "post", "deny")], pinned.Revision), ct);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.SetTopicAccessAsync(moderator.AccessToken, group, id, new([], pinned.Revision + 1), ct))).Status);
    }
    [Fact]
    public async Task Anonymous_form_responses_page_without_exposing_account_ids_or_losing_answers()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var ct = TestContext.Current.CancellationToken;
        var head = await Seed(host.Accounts, "space.paging.head");
        var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId);
        var topic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Анкеты", "list", "forms"), ct, true)).Topics, t => t.Kind == "forms");
        var question = Guid.NewGuid();
        var form = Assert.Single((await service.CreateFormAsync(head.AccessToken, group, topic.TopicId!.Value, new("Вопрос", "", null, true, [new(question, "Ответ", "shortText", false, [])]), ct)).Forms);
        await db.Accounts.ExecuteAsync($"""
            INSERT INTO {db.Accounts.QuotedSchema}.users(user_id,username,normalized_username,created_at,status)
            SELECT gen_random_uuid(),'paging.'||n,'paging.'||n,now(),'active' FROM generate_series(1,51) n;
            INSERT INTO {db.Configuration.QuotedMessages}.group_form_answers(form_id,user_id,answers,updated_at)
            SELECT '{form.FormId}',user_id,json_build_array(json_build_object('questionId','{question}','text',username,'choices',json_build_array()))::text,now()
            FROM {db.Accounts.QuotedSchema}.users WHERE username LIKE 'paging.%';
            """);
        var first = await service.FormResponsesAsync(head.AccessToken, group, form.FormId, ct);
        Assert.Equal(50, first.Responses.Count); Assert.Equal(51, first.TotalResponses); Assert.NotNull(first.NextCursor);
        var second = await service.FormResponsesAsync(head.AccessToken, group, form.FormId, ct, first.NextCursor);
        Assert.Single(second.Responses); Assert.Null(second.NextCursor);
        var all = first.Responses.Concat(second.Responses).ToArray();
        Assert.All(all, answer => Assert.Null(answer.RespondentId));
        Assert.Equal(51, all.Select(answer => answer.Answers[0].Text).Distinct().Count());
    }
    [Fact]
    public async Task Browser_space_routes_use_cookie_auth_and_the_same_contracts()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await db.Accounts.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
        await using var host = new WebAccountHost(db.Accounts, moduleSettings: new()
        {
            ["Communities:Enabled"] = "true",
            ["Communities:Schema"] = db.Schema
        });
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new Zapara.Contracts.Accounts.RegisterRequest("space_browser", WebAccountHost.Password));
        var session = await host.Login("space_browser");
        var user = session.GetProperty("user").GetProperty("userId").GetGuid(); var group = Guid.NewGuid();
        await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, user);
        var space = await host.Send("GET", $"/communities/{group}/space", 200);
        Assert.Equal(12, space.GetProperty("capabilities").GetProperty("maxRoles").GetInt32());
        var category = await host.Send("POST", $"/communities/{group}/space/categories", 200, new GroupCategoryRequest(null, "Учёба", 0));
        Assert.Single(category.GetProperty("categories").EnumerateArray());
        host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
        var topicPage = await host.Send("POST", $"/communities/{group}/topics", 201, new GroupTopicRequest("Задания", "book", "homework"));
        var topic = topicPage.GetProperty("topics").EnumerateArray().Single(t => t.GetProperty("kind").GetString() == "homework").GetProperty("topicId").GetGuid();
        await host.Send("POST", $"/communities/{group}/homework", 201, new HomeworkUpsert("Задача", "Текст", 0, topicId: topic));
        var copies = await host.Send("GET", $"/communities/{group}/homework/copies?topicId={topic}", 200);
        Assert.Equal(topic, Assert.Single(copies.EnumerateArray()).GetProperty("topicId").GetGuid());
    }
    [Fact]
    public async Task Hidden_topics_are_absent_from_lists_previews_media_and_legacy_chat()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "space.privacy.head"); var member = await Seed(host.Accounts, "space.privacy.member");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId);
        var chat = (await service.GroupHomeAsync(head.AccessToken, group, ct: TestContext.Current.CancellationToken)).GroupChat.ConversationId;
        var topic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Скрытый", "lock"), includeTyped: true, ct: TestContext.Current.CancellationToken)).Topics, t => t.TopicId is not null);
        var id = topic.TopicId!.Value;
        var media = await service.SendMediaAsync(head.AccessToken, chat, "file", "private.txt", [1, 2, 3], null, topicId: id, ct: TestContext.Current.CancellationToken);
        await service.SetTopicAccessAsync(head.AccessToken, group, id, new([new(null, "read", "deny")], topic.Revision), ct: TestContext.Current.CancellationToken);
        var desk = await service.CreateRoleAsync(head.AccessToken, group, new("Управляющий"), ct: TestContext.Current.CancellationToken); var role = Assert.Single(desk.Roles).RoleId;
        await service.SetRolePowerAsync(head.AccessToken, group, role, new("channels", true), ct: TestContext.Current.CancellationToken); await service.GrantRoleAsync(head.AccessToken, group, role, new(member.User.UserId), ct: TestContext.Current.CancellationToken);
        Assert.DoesNotContain((await host.Get<GroupSpaceResponse>($"/{group}/space", member.AccessToken)).Topics, t => t.TopicId == id);
        Assert.Empty((await service.ListMessagesAsync(member.AccessToken, chat, null, null, ct: TestContext.Current.CancellationToken)).Messages);
        Assert.Null((await service.GroupHomeAsync(member.AccessToken, group, ct: TestContext.Current.CancellationToken)).GroupChat.LastBody);
        Assert.DoesNotContain((await service.GroupAuditAsync(member.AccessToken, group, ct: TestContext.Current.CancellationToken)).Events, e => e.ObjectId == id);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ReadMediaAsync(member.AccessToken, chat, media.MessageId, ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.SendTopicMessageAsync(member.AccessToken, chat, new("Попытка", id), ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.RenameTopicAsync(member.AccessToken, group, id, new("Утечка", "lock"), ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ReactMessageAsync(member.AccessToken, chat, media.MessageId, new("like"), ct: TestContext.Current.CancellationToken))).Status);
    }

    [Fact]
    public async Task Forms_keep_answers_private_update_own_answer_and_archive_reversibly()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true); await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "space.forms.head"); var member = await Seed(host.Accounts, "space.forms.member"); var other = await Seed(host.Accounts, "space.forms.other");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId); await db.SeedMemberAsync(group, other.User.UserId);
        var category = Assert.Single((await service.SaveCategoryAsync(head.AccessToken, group, new(null, "Учёба", 1), ct: TestContext.Current.CancellationToken)).Categories);
        var topic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Анкеты", "list", "forms", template: "forms", categoryId: category.CategoryId), includeTyped: true, ct: TestContext.Current.CancellationToken)).Topics, t => t.TopicId is not null);
        var id = topic.TopicId!.Value; var question = Guid.NewGuid();
        var form = Assert.Single((await service.CreateFormAsync(head.AccessToken, group, id, new("Выбор", "Ответы видит автор", null, true, [new(question, "Вариант", "singleChoice", true, ["А", "Б"])]), ct: TestContext.Current.CancellationToken)).Forms);
        await service.SubmitFormAsync(member.AccessToken, group, form.FormId, new([new(question, null, ["А"])]), ct: TestContext.Current.CancellationToken);
        var own = await service.SubmitFormAsync(member.AccessToken, group, form.FormId, new([new(question, null, ["Б"])]), ct: TestContext.Current.CancellationToken);
        Assert.Equal("Б", Assert.Single(Assert.Single(own.OwnResponse!.Answers).Choices));
        var someone = Assert.Single((await service.FormsAsync(other.AccessToken, group, id, ct: TestContext.Current.CancellationToken)).Forms); Assert.Null(someone.OwnResponse); Assert.False(someone.CanViewResponses); Assert.Equal(0, someone.ResponseCount);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.FormResponsesAsync(other.AccessToken, group, form.FormId, ct: TestContext.Current.CancellationToken))).Status);
        var answers = Assert.Single((await service.FormResponsesAsync(head.AccessToken, group, form.FormId, ct: TestContext.Current.CancellationToken)).Responses); Assert.Null(answers.RespondentId);
        await service.ArchiveTopicAsync(head.AccessToken, group, id, new(true, topic.Revision), ct: TestContext.Current.CancellationToken);
        Assert.False(Assert.Single((await service.FormsAsync(member.AccessToken, group, id, ct: TestContext.Current.CancellationToken)).Forms).CanRespond);
        var archived = Assert.Single((await service.ArchivedTopicsAsync(head.AccessToken, group, ct: TestContext.Current.CancellationToken)).Topics);
        await service.ArchiveTopicAsync(head.AccessToken, group, id, new(false, archived.Revision), ct: TestContext.Current.CancellationToken);
        Assert.True(Assert.Single((await service.FormsAsync(member.AccessToken, group, id, ct: TestContext.Current.CancellationToken)).Forms).CanRespond);
        Assert.DoesNotContain((await service.GroupAuditAsync(head.AccessToken, group, ct: TestContext.Current.CancellationToken)).Events, e => e.Action.Contains("answer"));
        var fingerprint = await Fingerprint(db); await MessengerSchema.EnsureAsync(db.Accounts.DataSource, db.Configuration, TestContext.Current.CancellationToken); Assert.Equal(fingerprint, await Fingerprint(db));
    }

    [Fact]
    public async Task Delegators_cannot_edit_own_role_grant_above_self_or_escalate_via_vote()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true); await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>();
        var head = await Seed(host.Accounts, "space.roles.head"); var member = await Seed(host.Accounts, "space.roles.member"); var other = await Seed(host.Accounts, "space.roles.other");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId); await db.SeedMemberAsync(group, other.User.UserId);
        var top = Assert.Single((await service.CreateRoleAsync(head.AccessToken, group, new("Управляющий"), ct: TestContext.Current.CancellationToken)).Roles);
        await service.SaveRoleSettingsAsync(head.AccessToken, group, top.RoleId, new(top.Name, "shield", 10, top.Revision), ct: TestContext.Current.CancellationToken);
        await service.SetRolePowerAsync(head.AccessToken, group, top.RoleId, new("roles", true), ct: TestContext.Current.CancellationToken); await service.SetRolePowerAsync(head.AccessToken, group, top.RoleId, new("grants", true), ct: TestContext.Current.CancellationToken);
        await service.GrantRoleAsync(head.AccessToken, group, top.RoleId, new(member.User.UserId), ct: TestContext.Current.CancellationToken);
        var lower = (await service.CreateRoleAsync(member.AccessToken, group, new("Помощник"), ct: TestContext.Current.CancellationToken)).Roles.Single(r => r.RoleId != top.RoleId);
        await service.GrantRoleAsync(member.AccessToken, group, lower.RoleId, new(other.User.UserId), ct: TestContext.Current.CancellationToken);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.SetRolePowerAsync(member.AccessToken, group, lower.RoleId, new("channels", true), ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.RenameRoleAsync(member.AccessToken, group, top.RoleId, new("Своё имя"), ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.SaveRoleSettingsAsync(member.AccessToken, group, lower.RoleId, new(lower.Name, "user", 10, lower.Revision), ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ProposeChangeAsync(member.AccessToken, group, new("power", 2, lower.RoleId, Guid.Empty, "", "channels", true), ct: TestContext.Current.CancellationToken))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.GrantRoleAsync(member.AccessToken, group, lower.RoleId, new(member.User.UserId), ct: TestContext.Current.CancellationToken))).Status);
        var otherGroup = Guid.NewGuid(); await db.SeedCommunityAsync(otherGroup); await db.SeedMemberAsync(otherGroup, head.User.UserId);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.CreateRoleAsync(head.AccessToken, otherGroup, new("Чужая группа"), ct: TestContext.Current.CancellationToken))).Status);
    }

    [Fact]
    public async Task Shared_homework_deadline_roundtrips_and_old_rows_remain_readable()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true); await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>(); var head = await Seed(host.Accounts, "space.homework.head");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId);
        var deadline = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var hw = await service.PublishHomeworkAsync(head.AccessToken, group, new("Задача", "Решить", 0, deadline), ct: TestContext.Current.CancellationToken);
        Assert.Equal(deadline, (await service.GetHomeworkAsync(head.AccessToken, group, hw.HomeworkId, ct: TestContext.Current.CancellationToken)).DeadlineAt);
        Assert.Equal(deadline, Assert.Single(await service.ListHomeworkCopiesAsync(head.AccessToken, group, ct: TestContext.Current.CancellationToken)).DeadlineAt);
        Assert.Equal(deadline, Assert.Single(await service.ListHomeworkAsync(head.AccessToken, group, ct: TestContext.Current.CancellationToken)).DeadlineAt);
        await service.UpdateHomeworkAsync(head.AccessToken, group, hw.HomeworkId, new("Задача", "Без срока", hw.Revision), ct: TestContext.Current.CancellationToken);
        Assert.Null((await service.GetHomeworkAsync(head.AccessToken, group, hw.HomeworkId, ct: TestContext.Current.CancellationToken)).DeadlineAt);
    }
    private static async Task<string> Fingerprint(CommunityPostgresFixture db)
    {
        await using var connection = db.Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        return await CommunitiesSchemaShape.FingerprintAsync(connection, transaction, db.Configuration, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Legacy_http_responses_keep_old_keys_and_never_reinterpret_specialized_channels()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true); await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>(); var token = TestContext.Current.CancellationToken;
        var head = await Seed(host.Accounts, "space.legacy.head"); var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId);
        await service.CreateTopicAsync(head.AccessToken, group, new("Анкеты", "list", "forms"), ct: token);
        await service.CreateRoleAsync(head.AccessToken, group, new("Помощник"), token);
        await service.PublishHomeworkAsync(head.AccessToken, group, new("Задача", "Текст", 0, DateTimeOffset.UtcNow.AddDays(1)), token);
        using (var topics = System.Text.Json.JsonDocument.Parse(await host.Send("GET", $"/{group}/topics?typed=1", 200, head.AccessToken)))
        {
            ApiTestFactory.Keys(topics.RootElement, "topics", "canManageChannels");
            Assert.Single(topics.RootElement.GetProperty("topics").EnumerateArray());
            ApiTestFactory.Keys(topics.RootElement.GetProperty("topics")[0], "topicId", "title", "icon", "lastBody", "lastAuthor", "lastAt", "unread", "canDelete", "kind", "activeBallots", "description", "accent", "pinned", "writePolicy", "canPost");
        }
        using (var desk = System.Text.Json.JsonDocument.Parse(await host.Send("GET", $"/{group}/desk", 200, head.AccessToken)))
        {
            ApiTestFactory.Keys(desk.RootElement, "headman", "roles", "grants", "applicants", "powers", "mine");
            ApiTestFactory.Keys(desk.RootElement.GetProperty("roles")[0], "roleId", "name");
        }
        using (var homework = System.Text.Json.JsonDocument.Parse(await host.Send("GET", $"/{group}/homework", 200, head.AccessToken)))
            ApiTestFactory.Keys(homework.RootElement[0], "homeworkId", "communityId", "title", "body", "revision", "createdAt", "updatedAt");
        host.Client.DefaultRequestHeaders.Add("X-Zapara-Group-Space", "1");
        Assert.Contains((await host.Get<GroupTopicListResponse>($"/{group}/topics?typed=1", head.AccessToken)).Topics, t => t.Kind == "forms");
        Assert.NotNull(Assert.Single(await host.Get<HomeworkResponse[]>($"/{group}/homework", head.AccessToken)).DeadlineAt);
    }

    [Fact]
    public async Task Private_homework_and_ballot_channels_guard_direct_ids_and_read_only_votes()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true); await using var host = await CommunityApiTestHost.StartAsync(db);
        var service = host.App.Services.GetRequiredService<CommunityService>(); var token = TestContext.Current.CancellationToken;
        var head = await Seed(host.Accounts, "space.content.head"); var member = await Seed(host.Accounts, "space.content.member");
        var group = Guid.NewGuid(); await db.SeedCommunityAsync(group); await db.SeedStaffAsync(group, head.User.UserId); await db.SeedMemberAsync(group, member.User.UserId);
        var homeworkTopic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Задания", "book", "homework"), ct: token, includeTyped: true)).Topics, t => t.Kind == "homework");
        var hw = await service.PublishHomeworkAsync(head.AccessToken, group, new("Закрытая задача", "Текст задания", 0, topicId: homeworkTopic.TopicId), token);
        await service.SetTopicAccessAsync(head.AccessToken, group, homeworkTopic.TopicId!.Value, new([new(null, "read", "deny")], homeworkTopic.Revision), token);
        Assert.Empty(await service.ListHomeworkAsync(member.AccessToken, group, token));
        Assert.Empty(await service.ListHomeworkCopiesAsync(member.AccessToken, group, token));
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.GetHomeworkAsync(member.AccessToken, group, hw.HomeworkId, token))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.UpsertCompletionAsync(member.AccessToken, group, hw.HomeworkId, new(true, 0), token))).Status);
        var pollTopic = Assert.Single((await service.CreateTopicAsync(head.AccessToken, group, new("Опросы", "vote", "ballots"), ct: token, includeTyped: true)).Topics, t => t.Kind == "ballots");
        var pollId = pollTopic.TopicId!.Value;
        var ballot = Assert.Single((await service.OpenHeadmanBallotAsync(head.AccessToken, group, new("Выбор", ["Да", "Нет"], 2, pollId), token)).Ballots);
        await service.SetTopicAccessAsync(head.AccessToken, group, pollId, new([new(null, "post", "deny")], pollTopic.Revision), token);
        await service.VoteBallotAsync(member.AccessToken, group, ballot.BallotId, new(ballot.Options[0].OptionId), token);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.ProposeBallotAsync(member.AccessToken, group, new("Попытка", ["Да", "Нет"], 2, pollId), token))).Status);
        await service.SetTopicAccessAsync(head.AccessToken, group, pollId, new([new(null, "read", "deny")], pollTopic.Revision + 1), token);
        Assert.Empty((await service.BallotsAsync(member.AccessToken, group, token)).Ballots);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommunityServiceException>(() => service.VoteBallotAsync(member.AccessToken, group, ballot.BallotId, new(ballot.Options[0].OptionId), token))).Status);
        var preview = await service.PreviewPermissionsAsync(head.AccessToken, group, new(member.User.UserId, null), token);
        Assert.DoesNotContain(preview.Topics, t => t.TopicId == pollId || t.TopicId == homeworkTopic.TopicId);
    }
}
