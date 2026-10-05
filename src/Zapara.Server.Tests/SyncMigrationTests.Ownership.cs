using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncMigrationTests
{
    [Fact]
    public async Task Authorized_same_transaction_writes_private_schema_and_account_delete_cascades_every_table()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var service = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(service);
        var s = db.QuotedSchema;
        var manifest = Guid.NewGuid();
        await service.ExecuteAsync(session.AccessToken, async (context, ct) =>
        {
            await using var command = new NpgsqlCommand($"""
                INSERT INTO {s}.sync_state VALUES(@user,@epoch,1,0,@now);
                INSERT INTO {s}.sync_records VALUES(@user,'homework',@entity,1,true,@now,NULL);
                INSERT INTO {s}.sync_receipts VALUES(@user,@entity,@digest,409,@body,@now);
                INSERT INTO {s}.sync_changes VALUES(@user,1,jsonb_build_object(),@entity,@now);
                INSERT INTO {s}.sync_manifests VALUES(@manifest,@user,@epoch,1,@now,@now+interval '10 minutes',1);
                INSERT INTO {s}.sync_manifest_items VALUES(@manifest,1,jsonb_build_object());
                """, context.Connection, context.Transaction);
            command.Parameters.AddWithValue("user", context.UserId);
            command.Parameters.AddWithValue("epoch", Guid.NewGuid());
            command.Parameters.AddWithValue("entity", Guid.NewGuid());
            command.Parameters.AddWithValue("manifest", manifest);
            command.Parameters.AddWithValue("now", context.UtcNow);
            command.Parameters.AddWithValue("digest", new byte[32]);
            command.Parameters.AddWithValue("body", "{}"u8.ToArray());
            await command.ExecuteNonQueryAsync(ct);
            Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.sync_state"));
            return true;
        }, Ct);
        foreach (var table in PrivateTables)
            Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.{table}"));
        await db.Accounts.ExecuteAsync($"""
            DELETE FROM {db.Accounts.QuotedSchema}.access_tokens WHERE family_id='{session.FamilyId}';
            DELETE FROM {db.Accounts.QuotedSchema}.refresh_tokens WHERE family_id='{session.FamilyId}';
            DELETE FROM {db.Accounts.QuotedSchema}.session_families WHERE family_id='{session.FamilyId}';
            DELETE FROM {db.Accounts.QuotedSchema}.password_credentials WHERE user_id='{session.User.UserId}';
            DELETE FROM {db.Accounts.QuotedSchema}.users WHERE user_id='{session.User.UserId}';
            """);
        foreach (var table in PrivateTables)
            Assert.Equal(0L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.{table}"));
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.schema_migrations"));
    }

    [Fact]
    public async Task Ownership_constraints_reject_orphans_and_invalid_shapes()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var s = db.QuotedSchema;
        foreach (var sql in new[]
        {
            $"INSERT INTO {s}.sync_state VALUES(gen_random_uuid(),gen_random_uuid(),0,0,now())",
            $"INSERT INTO {s}.sync_manifest_items VALUES(gen_random_uuid(),1,'{{}}')"
        })
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Accounts.ExecuteAsync(sql))).SqlState);
        var service = new AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var session = await Seed(service);
        var user = session.User.UserId;
        await db.Accounts.ExecuteAsync($"INSERT INTO {s}.sync_state VALUES('{user}',gen_random_uuid(),0,0,now())");
        foreach (var sql in new[]
        {
            $"UPDATE {s}.sync_state SET sequence=-1",
            $"UPDATE {s}.sync_state SET min_after_sequence=1",
            $"INSERT INTO {s}.sync_records VALUES('{user}','settings',gen_random_uuid(),1,false,now(),'{{}}')",
            $"INSERT INTO {s}.sync_records VALUES('{user}','friend',gen_random_uuid(),0,false,now(),'{{}}')",
            $"INSERT INTO {s}.sync_records VALUES('{user}','homework',gen_random_uuid(),1,true,now(),'{{}}')",
            $"INSERT INTO {s}.sync_records VALUES('{user}','homework',gen_random_uuid(),1,false,now(),NULL)",
            $"INSERT INTO {s}.sync_receipts VALUES('{user}',gen_random_uuid(),decode('00','hex'),409,decode('7b7d','hex'),now())",
            $"INSERT INTO {s}.sync_receipts VALUES('{user}',gen_random_uuid(),decode(repeat('00',32),'hex'),410,decode('7b7d','hex'),now())"
        })
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Accounts.ExecuteAsync(sql))).SqlState);
        await db.Migrations.EnsureAsync(Ct);
    }

    private static readonly string[] PrivateTables = ["sync_state", "sync_records", "sync_receipts", "sync_changes", "sync_manifests", "sync_manifest_items"];
}
