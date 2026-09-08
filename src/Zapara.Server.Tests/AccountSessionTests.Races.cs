using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class AccountSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_profile_write_rechecks_revocation_or_exact_access_after_rotation(bool rotate)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(service);
        Assert.NotNull(await service.AuthenticateAsync(session.AccessToken, TestContext.Current.CancellationToken));
        await using var gate = await AccountDatabaseGate.LockUser(db, session.User.UserId);
        Task first = rotate ? service.RefreshAsync(session.RefreshToken, TestContext.Current.CancellationToken)
            : service.LogoutAsync(session.AccessToken, TestContext.Current.CancellationToken);
        await AccountDatabaseGate.WaitFor(db, 1);
        var queued = service.UpdateProfileAsync(session.AccessToken, new("Не записывать"), TestContext.Current.CancellationToken);
        await AccountDatabaseGate.WaitFor(db, 2);
        await gate.Commit();
        await first;
        await Failure(AccountFailure.InvalidSession, () => queued);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users WHERE display_name IS NULL"));
    }

    [Fact]
    public async Task Cancellation_while_waiting_on_user_lock_does_not_mutate()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(service);
        await using var gate = await AccountDatabaseGate.LockUser(db, session.User.UserId);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var queued = service.UpdateProfileAsync(session.AccessToken, new("Не записывать"), cancel.Token);
        await AccountDatabaseGate.WaitFor(db, 1);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await gate.Commit();
        Assert.Null((await service.GetMeAsync(session.AccessToken, TestContext.Current.CancellationToken)).User.DisplayName);
    }

    [Fact]
    public async Task Rotation_during_password_work_rejects_stale_password_change()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var normal = new AccountService(db.DataSource, db.Configuration, clock);
        var session = await Seed(normal);
        using var controlled = new AccountControlledHasher();
        controlled.AfterVerify = controlled.Block;
        var racing = new AccountService(db.DataSource, db.Configuration, clock, new(controlled));
        var change = Task.Run(() => racing.ChangePasswordAsync(session.AccessToken, new(Password, NewPassword)), TestContext.Current.CancellationToken);
        try
        {
            await controlled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotNull(await normal.RefreshAsync(session.RefreshToken, TestContext.Current.CancellationToken));
        }
        finally { controlled.Release.Set(); }
        await Failure(AccountFailure.InvalidSession, () => change);
        Assert.NotNull(await normal.LoginAsync(Login(), TestContext.Current.CancellationToken));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT credential_version FROM {db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task Multiple_families_for_same_device_are_allowed_and_expired_families_are_not_listed()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var service = new AccountService(db.DataSource, db.Configuration, clock);
        var first = await Seed(service);
        clock.Now += TimeSpan.FromDays(30);
        var device = Guid.NewGuid();
        var a = await service.LoginAsync(Login(device: device), TestContext.Current.CancellationToken);
        var b = await service.LoginAsync(Login(device: device), TestContext.Current.CancellationToken);
        Assert.NotEqual(a.FamilyId, b.FamilyId);
        var devices = await service.ListDevicesAsync(a.AccessToken, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, devices.Devices.Count);
        Assert.All(devices.Devices, x => Assert.Equal(device, x.DeviceId));
        Assert.Single(devices.Devices, x => x.IsCurrent);
        Assert.DoesNotContain(devices.Devices, x => x.FamilyId == first.FamilyId);
    }

    [Fact]
    public async Task Password_change_committing_first_prevents_queued_refresh()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(service);
        var key = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.hold_password_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action='password_change' THEN PERFORM pg_advisory_xact_lock({key}); END IF; RETURN NEW; END $$;
            CREATE TRIGGER hold_password_audit BEFORE INSERT ON {db.QuotedSchema}.account_security_events
            FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.hold_password_audit()
            """);
        await using var blocker = db.DataSource.CreateConnection();
        await blocker.OpenAsync(ct);
        await using var tx = await blocker.BeginTransactionAsync(ct);
        await using (var command = new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", blocker, tx))
        {
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync(ct);
        }
        var change = service.ChangePasswordAsync(session.AccessToken, new(Password, NewPassword), ct);
        await AccountDatabaseGate.WaitFor(db, 1);
        var refresh = service.RefreshAsync(session.RefreshToken, ct);
        await AccountDatabaseGate.WaitFor(db, 2);
        await tx.CommitAsync(ct);
        await change;
        await Failure(AccountFailure.InvalidSession, () => refresh);
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(session.AccessToken));
        Assert.NotNull(await service.LoginAsync(Login(password: NewPassword), ct));
    }
}
