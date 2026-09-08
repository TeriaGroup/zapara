using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class CommunityPostgresFixture : IAsyncDisposable
{
    public AccountsPostgresFixture Accounts { get; }
    public string Schema { get; } = "com_test_" + Guid.NewGuid().ToString("N");
    public string QuotedSchema => $"\"{Schema}\"";
    public CommunitiesConfiguration Configuration { get; }
    public CommunitiesMigrations Migrations => new(Accounts.DataSource, Configuration);
    private CommunityPostgresFixture(AccountsPostgresFixture accounts, bool? accountsContainsCom = null)
    {
        Accounts = accounts;
        if (accountsContainsCom is not null)
            Schema = accountsContainsCom.Value ? accounts.Schema[4..] : "com_" + accounts.Schema;
        Configuration = Options(Schema, accounts.Schema);
    }
    public static CommunitiesConfiguration Options(string schema, string accountsSchema)
        => CommunitiesConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Communities:Enabled"] = "true", ["Communities:Schema"] = schema,
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = accountsSchema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")
        }).Build(), true);
    public static async Task<CommunityPostgresFixture> CreateAsync(bool initialize = false, bool? accountsContainsCom = null)
    {
        var db = new CommunityPostgresFixture(await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true, targetVersion: 2), accountsContainsCom);
        try
        {
            await db.Accounts.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}");
            Console.WriteLine($"CREATE {db.Schema}");
            if (initialize) await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }
    public async Task SeedCommunityAsync(Guid communityId, string name = "Группа О3313", string description = "Сообщество учебной группы")
        => await Accounts.ExecuteAsync($"""
            INSERT INTO {QuotedSchema}.communities(community_id,name,description,revision,created_at,updated_at)
            VALUES('{communityId}','{name}','{description}',1,TIMESTAMPTZ '2026-09-08 12:00:00+00',TIMESTAMPTZ '2026-09-08 12:00:00+00')
            """);
    public async Task SeedCatalogAsync(Guid communityId, string groupId = "O3313", string groupName = "О3313")
        => await Accounts.ExecuteAsync($"""
            INSERT INTO {QuotedSchema}.catalog_maps(map_id,community_id,group_id,group_name,created_at)
            VALUES('{Guid.NewGuid()}','{communityId}','{groupId}','{groupName}',TIMESTAMPTZ '2026-09-08 12:00:00+00')
            """);
    public async Task SeedStaffAsync(Guid communityId, Guid userId, string role = "headman")
        => await Accounts.ExecuteAsync($"""
            INSERT INTO {QuotedSchema}.memberships(community_id,user_id,role,status,created_at,revoked_at)
            VALUES('{communityId}','{userId}','{role}','active',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL);
            INSERT INTO {QuotedSchema}.staff_assignments(assignment_id,community_id,user_id,role,assigned_at,revoked_at)
            VALUES('{Guid.NewGuid()}','{communityId}','{userId}','{role}',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL)
            """);
    public async Task SeedMemberAsync(Guid communityId, Guid userId)
        => await Accounts.ExecuteAsync($"""
            INSERT INTO {QuotedSchema}.memberships(community_id,user_id,role,status,created_at,revoked_at)
            VALUES('{communityId}','{userId}','member','active',TIMESTAMPTZ '2026-09-08 12:00:00+00',NULL)
            """);
    public async Task RevokeStaffAsync(Guid communityId, Guid userId)
    {
        await using var connection = Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var tx = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var lockCommunity = new NpgsqlCommand($"SELECT community_id FROM {QuotedSchema}.communities WHERE community_id=@id FOR UPDATE", connection, tx))
        {
            lockCommunity.Parameters.AddWithValue("id", communityId);
            await lockCommunity.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }
        await using (var membership = new NpgsqlCommand($"""
            UPDATE {QuotedSchema}.memberships SET role='member' WHERE community_id=@c AND user_id=@u AND status='active'
            """, connection, tx))
        {
            membership.Parameters.AddWithValue("c", communityId);
            membership.Parameters.AddWithValue("u", userId);
            await membership.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        await using (var staff = new NpgsqlCommand($"""
            UPDATE {QuotedSchema}.staff_assignments SET revoked_at=TIMESTAMPTZ '2026-09-08 12:05:00+00'
            WHERE community_id=@c AND user_id=@u AND revoked_at IS NULL
            """, connection, tx))
        {
            staff.Parameters.AddWithValue("c", communityId);
            staff.Parameters.AddWithValue("u", userId);
            await staff.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        await tx.CommitAsync(TestContext.Current.CancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Accounts.ExecuteAsync($"DROP SCHEMA IF EXISTS {QuotedSchema} CASCADE");
            var count = await Accounts.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{Schema}'");
            Console.WriteLine($"TEARDOWN {Schema} remaining={count}");
            Assert.Equal(0L, count);
        }
        finally { await Accounts.DisposeAsync(); }
    }
}
