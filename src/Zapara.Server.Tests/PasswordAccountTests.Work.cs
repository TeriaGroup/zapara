using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class PasswordAccountTests
{
    [Fact]
    public async Task Unknown_disabled_and_locked_attempts_each_do_one_dummy_verification()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        using var controlled = new AccountControlledHasher();
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock(), new(controlled));
        await Seed(service);
        foreach (var state in new[] { "unknown", "disabled", "locked" })
        {
            await db.ExecuteAsync($"UPDATE {db.QuotedSchema}.users SET status='{(state == "disabled" ? "disabled" : "active")}'");
            if (state == "locked") await db.ExecuteAsync($"UPDATE {db.QuotedSchema}.password_credentials SET locked_until='2026-09-08T12:15:00Z'");
            var before = controlled.VerifyCalls;
            await Failure(AccountFailure.InvalidCredentials, () => service.LoginAsync(Login(state == "unknown" ? "missing.user" : "test.user")));
            Assert.Equal(before + 1, controlled.VerifyCalls);
        }
    }

    [Fact]
    public async Task Ninth_hash_is_rate_limited_without_queue_and_cancellation_does_not_insert()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        using var controlled = new AccountControlledHasher();
        using var allEntered = new CountdownEvent(8);
        controlled.BeforeHash = () => { allEntered.Signal(); controlled.Block(); };
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock(), new(controlled));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = Enumerable.Range(0, 8).Select(i => Task.Factory.StartNew(
            () => service.RegisterAsync(new("limit.user" + i, Password), cancel.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()).ToArray();
        try
        {
            Assert.True(allEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            await Failure(AccountFailure.RateLimited, () => service.RegisterAsync(new("ninth.user", Password)));
            Assert.Equal(8, controlled.HashCalls);
            cancel.Cancel();
        }
        finally { controlled.Release.Set(); }
        foreach (var task in running) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verified_old_login_or_rehash_cannot_overwrite_concurrent_password_change(bool oldHash)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var clock = new AccountClock();
        var normal = new AccountService(db.DataSource, db.Configuration, clock);
        var session = await Seed(normal);
        if (oldHash)
        {
            var legacy = new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions { IterationCount = 1000 }));
            var hash = legacy.HashPassword(new(session.User.UserId), Password);
            await using var connection = db.DataSource.CreateConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new Npgsql.NpgsqlCommand($"UPDATE {db.QuotedSchema}.password_credentials SET password_hash=@hash", connection);
            command.Parameters.AddWithValue("hash", hash);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        using var controlled = new AccountControlledHasher();
        controlled.AfterVerify = controlled.Block;
        var racing = new AccountService(db.DataSource, db.Configuration, clock, new(controlled));
        var login = Task.Run(() => racing.LoginAsync(Login()), TestContext.Current.CancellationToken);
        try
        {
            await controlled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await normal.ChangePasswordAsync(session.AccessToken, new(Password, NewPassword), TestContext.Current.CancellationToken);
        }
        finally { controlled.Release.Set(); }
        await Failure(AccountFailure.InvalidCredentials, () => login);
        Assert.NotNull(await normal.LoginAsync(Login(password: NewPassword), TestContext.Current.CancellationToken));
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT credential_version FROM {db.QuotedSchema}.users"));
    }

    [Fact]
    public async Task Legacy_hash_is_upgraded_and_official_hash_benchmark_records_five_samples()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        var session = await Seed(service);
        var legacy = new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions { IterationCount = 1000 }));
        var current = new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions
            { IterationCount = 100000, CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3 }));
        var user = new AccountUser(session.User.UserId);
        var hash = legacy.HashPassword(user, Password);
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, current.VerifyHashedPassword(user, hash, Password));
        await using (var connection = db.DataSource.CreateConnection())
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new Npgsql.NpgsqlCommand($"UPDATE {db.QuotedSchema}.password_credentials SET password_hash=@hash", connection);
            command.Parameters.AddWithValue("hash", hash);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        Assert.NotNull(await service.LoginAsync(Login(), TestContext.Current.CancellationToken));
        var upgraded = await db.ScalarAsync<string>($"SELECT password_hash FROM {db.QuotedSchema}.password_credentials");
        Assert.True(upgraded != hash);
        Assert.Equal(PasswordVerificationResult.Success, current.VerifyHashedPassword(user, upgraded, Password));
        _ = current.HashPassword(user, Password);
        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            _ = current.HashPassword(user, Password);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"IdentityV3 iteration100000 hash milliseconds=[{string.Join(",", samples.Select(x => x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}]; median={samples.Order().ElementAt(2):F2}");
    }
}
