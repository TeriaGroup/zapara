using Xunit;
using Zapara.Server.Admin;
using Zapara.Server.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class AdminCommunityMembershipTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_join_acceptance_and_staff_assignment_add_group_chat_members_immediately()
    {
        await using var db = await AdminPostgresFixture.CreateAsync(true);
        var accounts = db.AccountService;
        var actor = await Seed(accounts, "admin.chat.actor");
        var joining = await Seed(accounts, "admin.chat.joining");
        var staff = await Seed(accounts, "admin.chat.staff");
        var communityId = Guid.NewGuid();
        await db.Communities.SeedCommunityAsync(communityId);
        var service = new CommunityService(accounts, db.Communities.Configuration);
        var request = await service.RequestJoinAsync(joining.AccessToken, communityId, Ct);
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        await using (var connection = db.Accounts.DataSource.CreateConnection())
        {
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            var work = new AdminWork(connection, tx, db.Configuration, actor.User.UserId, Guid.Empty, 0, now, Ct);
            await work.AcceptJoinAsync(communityId, request.RequestId);
            await work.AssignStaffAsync(communityId, staff.User.UserId, "headman");
            await tx.CommitAsync(Ct);
        }

        var msg = $"\"{db.Communities.Configuration.MessagesSchema}\"";
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {msg}.conversations WHERE kind='group' AND community_id='{communityId}'"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {msg}.conversation_members m JOIN {msg}.conversations c USING (conversation_id) WHERE c.community_id='{communityId}' AND m.user_id='{joining.User.UserId}'"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {msg}.conversation_members m JOIN {msg}.conversations c USING (conversation_id) WHERE c.community_id='{communityId}' AND m.user_id='{staff.User.UserId}'"));
    }
}
