using Xunit;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class MessengerSchemaUpgradeTests
{
    [Fact]
    public async Task Existing_topics_and_ballots_survive_channel_columns_and_repeated_ensure()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        var group = Guid.NewGuid();
        var topic = Guid.NewGuid();
        var ballot = Guid.NewGuid();
        var role = Guid.NewGuid();
        await db.SeedCommunityAsync(group);
        var schema = db.Configuration.QuotedMessages;

        await db.Accounts.ExecuteAsync($"""
            ALTER TABLE {schema}.ballots DROP COLUMN topic_id;
            ALTER TABLE {schema}.group_topics DROP COLUMN kind;
            ALTER TABLE {schema}.group_topics DROP COLUMN description;
            ALTER TABLE {schema}.group_topics DROP COLUMN accent;
            ALTER TABLE {schema}.group_topics DROP COLUMN pinned;
            ALTER TABLE {schema}.group_topics DROP COLUMN write_policy;
            ALTER TABLE {schema}.group_role_powers DROP CONSTRAINT group_role_powers_power_check;
            ALTER TABLE {schema}.group_role_powers ADD CONSTRAINT group_role_powers_power_check
                CHECK (power IN ('joins','exclude','roles','grants','ballots','close'));
            INSERT INTO {schema}.group_topics(topic_id,community_id,title,icon,created_by,created_at)
            VALUES('{topic}','{group}','Старый раздел','📚',NULL,TIMESTAMPTZ '2026-09-08 12:00:00+00');
            INSERT INTO {schema}.ballots(ballot_id,community_id,question,origin,status,deadline_at,opened_at,created_by,created_at)
            VALUES('{ballot}','{group}','Старый вопрос','headman','closed',TIMESTAMPTZ '2026-09-08 13:00:00+00',NULL,NULL,TIMESTAMPTZ '2026-09-08 12:00:00+00');
            """);

        await MessengerSchema.EnsureAsync(db.Accounts.DataSource, db.Configuration, TestContext.Current.CancellationToken);
        await MessengerSchema.EnsureAsync(db.Accounts.DataSource, db.Configuration, TestContext.Current.CancellationToken);

        Assert.Equal("chat", await db.Accounts.ScalarAsync<string>($"SELECT kind FROM {schema}.group_topics WHERE topic_id='{topic}'"));
        Assert.Equal("", await db.Accounts.ScalarAsync<string>($"SELECT description FROM {schema}.group_topics WHERE topic_id='{topic}'"));
        Assert.Equal("default", await db.Accounts.ScalarAsync<string>($"SELECT accent FROM {schema}.group_topics WHERE topic_id='{topic}'"));
        Assert.False(await db.Accounts.ScalarAsync<bool>($"SELECT pinned FROM {schema}.group_topics WHERE topic_id='{topic}'"));
        Assert.Equal("all", await db.Accounts.ScalarAsync<string>($"SELECT write_policy FROM {schema}.group_topics WHERE topic_id='{topic}'"));
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {schema}.ballots WHERE ballot_id='{ballot}' AND topic_id IS NULL"));
        await db.Accounts.ExecuteAsync($"""
            INSERT INTO {schema}.group_roles(role_id,community_id,name,created_at)
            VALUES('{role}','{group}','Помощник',TIMESTAMPTZ '2026-09-08 12:00:00+00');
            INSERT INTO {schema}.group_role_powers(role_id,power) VALUES('{role}','channels');
            """);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {schema}.group_role_powers WHERE role_id='{role}' AND power='channels'"));
    }
}
