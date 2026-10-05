using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Communities;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Communities;
using Zapara.Server.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class LifecycleParticipationTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = new(2026, 9, 8, 12, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task Export_includes_own_sync_and_community_data_and_never_other_users_private_data()
    {
        await using var harness = await LifecycleHarness.CreateAsync(output.WriteLine, sync: true, communities: true);
        await using var host = new LifecycleApiHost(harness, clock: new AccountClock());
        await host.Register("owner.user");
        await host.Register("other.user");
        var owner = await host.Login("owner.user");
        var other = await host.Login("other.user");
        var accounts = host.Factory.Services.GetRequiredService<AccountService>();
        var sync = new SyncService(accounts, harness.Sync!);
        var communities = new CommunityService(accounts, harness.Communities!);
        var ownerMeta = await sync.MetadataAsync(owner.AccessToken, Ct);
        var otherMeta = await sync.MetadataAsync(other.AccessToken, Ct);
        var ownerHomeworkId = Guid.NewGuid();
        var otherHomeworkId = Guid.NewGuid();
        Assert.Equal(200, (await sync.MutateAsync(owner.AccessToken, Put(ownerMeta, ownerHomeworkId, "Секрет владельца"), Ct)).Status);
        Assert.Equal(200, (await sync.MutateAsync(other.AccessToken, Put(otherMeta, otherHomeworkId, "Секрет чужого"), Ct)).Status);
        var communityId = Guid.NewGuid();
        await SeedCommunity(harness, communityId);
        await harness.Accounts.ExecuteAsync($"""
            INSERT INTO "{harness.CommunitiesSchema}".memberships(community_id,user_id,role,status,created_at,revoked_at)
            VALUES('{communityId}','{owner.User.UserId}','headman','active',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL);
            INSERT INTO "{harness.CommunitiesSchema}".staff_assignments(assignment_id,community_id,user_id,role,assigned_at,revoked_at)
            VALUES('{Guid.NewGuid()}','{communityId}','{owner.User.UserId}','headman',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL)
            """);
        await harness.Accounts.ExecuteAsync($"""
            INSERT INTO "{harness.CommunitiesSchema}".memberships(community_id,user_id,role,status,created_at,revoked_at)
            VALUES('{communityId}','{other.User.UserId}','member','active',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL)
            """);
        var homework = await communities.PublishHomeworkAsync(owner.AccessToken, communityId, new("ДЗ", "Текст задания", 0), Ct);
        var announcement = await communities.PublishAnnouncementAsync(owner.AccessToken, communityId, new("Объявление", "Текст", 0), Ct);
        var poll = await communities.PublishPollAsync(owner.AccessToken, communityId, new("Вопрос?", Deadline, ["Да", "Нет"], 0), Ct);
        await communities.UpsertCompletionAsync(owner.AccessToken, communityId, homework.HomeworkId, new(true, 0), Ct);
        await communities.UpsertCompletionAsync(other.AccessToken, communityId, homework.HomeworkId, new(true, 0), Ct);
        await communities.VoteAsync(owner.AccessToken, communityId, poll.PollId, new(poll.Options[0].OptionId), Ct);
        await communities.VoteAsync(other.AccessToken, communityId, poll.PollId, new(poll.Options[1].OptionId), Ct);
        var proof = await host.Proof(owner, "export");
        var created = await host.Send("POST", "/account/exports", 202, new ProofRequest(proof), owner.AccessToken);
        var exportId = created.GetProperty("exportId").GetGuid();
        var (payload, _, _) = await host.Download("/account/exports/" + exportId.ToString("D") + "/download", 200, owner.AccessToken);
        var raw = payload.GetRawText();
        Assert.Contains("Секрет владельца", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Секрет чужого", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(other.User.UserId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.Contains(ownerHomeworkId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.DoesNotContain(otherHomeworkId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.Contains(homework.HomeworkId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.Contains(announcement.AnnouncementId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.Contains(poll.PollId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("memberships").ValueKind);
        Assert.Equal(communityId, payload.GetProperty("memberships")[0].GetProperty("communityId").GetGuid());
        Assert.Equal(1, payload.GetProperty("completions").GetArrayLength());
        Assert.Equal(1, payload.GetProperty("votes").GetArrayLength());
        Assert.Equal(poll.Options[0].OptionId, payload.GetProperty("votes")[0].GetProperty("optionId").GetGuid());
        Assert.DoesNotContain(poll.Options[1].OptionId.ToString("D"), raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_removes_personal_data_deidentifies_publications_and_keeps_other_users()
    {
        await using var harness = await LifecycleHarness.CreateAsync(output.WriteLine, sync: true, communities: true);
        await using var host = new LifecycleApiHost(harness, clock: new AccountClock());
        await host.Register("gone.user");
        await host.Register("stay.user");
        var gone = await host.Login("gone.user");
        var stay = await host.Login("stay.user");
        var accounts = host.Factory.Services.GetRequiredService<AccountService>();
        var sync = new SyncService(accounts, harness.Sync!);
        var communities = new CommunityService(accounts, harness.Communities!);
        var goneMeta = await sync.MetadataAsync(gone.AccessToken, Ct);
        var stayMeta = await sync.MetadataAsync(stay.AccessToken, Ct);
        Assert.Equal(200, (await sync.MutateAsync(gone.AccessToken, Put(goneMeta, Guid.NewGuid(), "Удаляемое"), Ct)).Status);
        Assert.Equal(200, (await sync.MutateAsync(stay.AccessToken, Put(stayMeta, Guid.NewGuid(), "Остаётся"), Ct)).Status);
        var communityId = Guid.NewGuid();
        await SeedCommunity(harness, communityId);
        await harness.Accounts.ExecuteAsync($"""
            INSERT INTO "{harness.CommunitiesSchema}".memberships(community_id,user_id,role,status,created_at,revoked_at)
            VALUES('{communityId}','{gone.User.UserId}','headman','active',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL);
            INSERT INTO "{harness.CommunitiesSchema}".staff_assignments(assignment_id,community_id,user_id,role,assigned_at,revoked_at)
            VALUES('{Guid.NewGuid()}','{communityId}','{gone.User.UserId}','headman',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL)
            """);
        await harness.Accounts.ExecuteAsync($"""
            INSERT INTO "{harness.CommunitiesSchema}".memberships(community_id,user_id,role,status,created_at,revoked_at)
            VALUES('{communityId}','{stay.User.UserId}','member','active',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL)
            """);
        var homework = await communities.PublishHomeworkAsync(gone.AccessToken, communityId, new("ДЗ", "Текст", 0), Ct);
        await communities.UpsertCompletionAsync(gone.AccessToken, communityId, homework.HomeworkId, new(true, 0), Ct);
        await communities.UpsertCompletionAsync(stay.AccessToken, communityId, homework.HomeworkId, new(true, 0), Ct);
        var poll = await communities.PublishPollAsync(gone.AccessToken, communityId, new("Вопрос?", Deadline, ["Да", "Нет"], 0), Ct);
        await communities.VoteAsync(gone.AccessToken, communityId, poll.PollId, new(poll.Options[0].OptionId), Ct);
        await communities.VoteAsync(stay.AccessToken, communityId, poll.PollId, new(poll.Options[1].OptionId), Ct);
        var proof = await host.Proof(gone, "delete_account");
        var accepted = await host.Send("DELETE", "/account", 202, new ProofRequest(proof), gone.AccessToken);
        Assert.False(accepted.GetProperty("remoteWipe").GetBoolean());
        var com = $"\"{harness.CommunitiesSchema}\"";
        var syncSchema = $"\"{harness.SyncSchema}\"";
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {syncSchema}.sync_state WHERE user_id='{gone.User.UserId}'"));
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {syncSchema}.sync_state WHERE user_id='{stay.User.UserId}'"));
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.memberships WHERE user_id='{gone.User.UserId}'"));
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.memberships WHERE user_id='{stay.User.UserId}'"));
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.shared_homework_completion WHERE user_id='{gone.User.UserId}'"));
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.shared_homework_completion WHERE user_id='{stay.User.UserId}'"));
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.votes WHERE user_id='{gone.User.UserId}'"));
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.votes WHERE user_id='{stay.User.UserId}'"));
        Assert.True(await harness.Accounts.ScalarAsync<object?>($"SELECT created_by FROM {com}.shared_homework WHERE homework_id='{homework.HomeworkId}'") is null or DBNull);
        Assert.True(await harness.Accounts.ScalarAsync<object?>($"SELECT created_by FROM {com}.polls WHERE poll_id='{poll.PollId}'") is null or DBNull);
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {com}.shared_homework"));
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {harness.Accounts.QuotedSchema}.deletion_manifests WHERE user_id='{gone.User.UserId}'"));
        await host.Send("GET", "/account/me", 200, bearer: stay.AccessToken);
        var stayExportProof = await host.Proof(stay, "export");
        var stayExport = await host.Send("POST", "/account/exports", 202, new ProofRequest(stayExportProof), stay.AccessToken);
        var (stayPayload, _, _) = await host.Download("/account/exports/" + stayExport.GetProperty("exportId").GetGuid().ToString("D") + "/download", 200, stay.AccessToken);
        Assert.Contains("Остаётся", stayPayload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Удаляемое", stayPayload.GetRawText(), StringComparison.Ordinal);
    }

    private static SyncMutation Put(SyncMetadata metadata, Guid id, string text)
        => new(metadata.SyncEpoch, Guid.NewGuid(), "homework", id, 0, "upsert",
            new HomeworkValue("Б1.О Математика", "б1.о математика", text, 2, Created, new DateOnly(2026, 8, 31)));

    private static Task SeedCommunity(LifecycleHarness harness, Guid communityId)
        => harness.Accounts.ExecuteAsync($"""
            INSERT INTO "{harness.CommunitiesSchema}".communities(community_id,name,description,revision,created_at,updated_at)
            VALUES('{communityId}','Группа О3313','Сообщество учебной группы',1,TIMESTAMPTZ '2026-09-08 12:00:00+00',TIMESTAMPTZ '2026-09-08 12:00:00+00')
            """);
}
