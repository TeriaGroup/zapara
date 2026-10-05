using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class WebAccountMigrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UpgradePreservesNativeSessionAndAddsBrowserPlatform()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 4);
        var service = new AccountService(db.DataSource, db.Configuration, TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;
        var user = await service.RegisterAsync(new("migration_user", "Synthetic test password 123!"), ct);
        var native = await service.LoginAsync(new("migration_user", "Synthetic test password 123!", new(Guid.NewGuid(), "Windows", "windows")), ct);
        await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(user.UserId, (await service.AuthenticateAsync(native.AccessToken, ct)).User.UserId);
        var web = await service.LoginAsync(new("migration_user", "Synthetic test password 123!", new(Guid.NewGuid(), "Browser", "web")), ct);
        Assert.Equal(native.FamilyId, Assert.Single((await service.ListDevicesAsync(native.AccessToken, limit: 1, ct: ct)).Devices).FamilyId);
        var modern = await service.ListDevicesAsync(native.AccessToken, ct: ct, includeWeb: true);
        Assert.Equal(2, modern.Devices.Count);
        Assert.Contains(modern.Devices, d => d.FamilyId == web.FamilyId && d.Platform == "web");
        var zeroHash = new string('0', 64);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.ExecuteAsync($"""
            INSERT INTO {db.QuotedSchema}.oauth_transactions(transaction_id,owner_id,purpose,provider,native_challenge,state_hash,
                initiator_user_id,status,expires_at,return_kind,device_id,device_name,platform)
            VALUES('{Guid.NewGuid()}','{Guid.NewGuid()}','login','yandex',decode('{zeroHash}','hex'),decode('{zeroHash}','hex'),
                '{user.UserId}','pending',now()+interval '10 minutes','web','{Guid.NewGuid()}','Browser','web')
            """));
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task WebSchemaMatchesGenuinePostgresCatalog()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 4);
        await db.ExecuteAsync(AccountsMigrations.MigrationSql(5).Replace("__SCHEMA__", db.QuotedSchema, StringComparison.Ordinal));
        var actual = await db.FingerprintAsync();
        output.WriteLine("Web PostgreSQL catalog receipt: " + actual);
        Assert.Equal(AccountsSchemaShape.WebFingerprint, actual);
    }

    [Fact]
    public void DeviceCursorCannotCrossVersionOrAccount()
    {
        var actor = Guid.NewGuid();
        var position = new AccountDeviceCursor(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid());
        var native = position.Encode(actor);
        var modern = position.Encode(actor, true);
        Assert.Equal(position, AccountDeviceCursor.Parse(native, actor));
        Assert.Equal(position, AccountDeviceCursor.Parse(modern, actor, true));
        Assert.Throws<AccountServiceException>(() => AccountDeviceCursor.Parse(native, actor, true));
        Assert.Throws<AccountServiceException>(() => AccountDeviceCursor.Parse(modern, actor));
        Assert.Throws<AccountServiceException>(() => AccountDeviceCursor.Parse(modern, Guid.NewGuid(), true));
    }

    [Fact]
    public async Task BrowserRefreshRollsBackConsumptionWhenSessionPersistenceFails()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 5);
        var service = new AccountService(db.DataSource, db.Configuration, TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;
        await service.RegisterAsync(new("atomic_browser", "Synthetic test password 123!"), ct);
        var session = await service.LoginAsync(new("atomic_browser", "Synthetic test password 123!", new(Guid.NewGuid(), "Browser", "web")), ct);
        var error = await Assert.ThrowsAsync<AccountServiceException>(() => service.RefreshBrowserSessionAsync(session.RefreshToken, new byte[32], _ => "protected", ct));
        Assert.Equal(AccountFailure.InvalidSession, error.Failure);
        var refreshed = await service.RefreshAsync(session.RefreshToken, ct);
        Assert.Equal(session.FamilyId, refreshed.FamilyId);
        Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);
    }
}
