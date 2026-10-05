using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class AccountServiceTests
{
    [Fact]
    public async Task Runtime_is_schema_qualified_and_leaves_adjacent_accounts_and_timetable_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var timetable = await PostgresFixture.CreateAsync(Console.WriteLine, ct: ct);
        var timetableBefore = await ApiTestFactory.DatabaseStateAsync(timetable);
        await using var adjacent = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var adjacentService = new AccountService(adjacent.DataSource, adjacent.Configuration, new AccountClock());
        await Seed(adjacentService);
        var adjacentBefore = await AccountState(adjacent);
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"))
            { SearchPath = adjacent.QuotedSchema };
        var config = AccountsConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Schema, ["ConnectionStrings:Accounts"] = builder.ConnectionString
        }).Build());
        await using var source = config.CreateDataSource();
        var service = new AccountService(source, config, new AccountClock());
        var session = await Seed(service);
        var next = await service.RefreshAsync(session.RefreshToken, ct);
        await service.UpdateProfileAsync(next.AccessToken, new("Тест"), ct);
        await service.ChangePasswordAsync(next.AccessToken, new(Password, NewPassword), ct);
        await db.Migrations.EnsureAsync(ct);
        await timetable.Store.EnsureSchemaAsync(ct);
        Assert.True(adjacentBefore == await AccountState(adjacent), "Adjacent identical table names must not be touched.");
        Assert.True(timetableBefore == await ApiTestFactory.DatabaseStateAsync(timetable), "Timetable rows must not change.");
        var rows = await AccountState(db);
        Assert.True(new[] { Password, NewPassword, session.AccessToken, session.RefreshToken, next.AccessToken, next.RefreshToken }
            .All(secret => !rows.Contains(secret, StringComparison.Ordinal)), "No plaintext credentials in account rows.");
        TestContext.Current.TestOutputHelper!.WriteLine("SQL runtime schema qualification=true; adjacent rows unchanged=true; timetable exact verifier and rows unchanged=true; plaintext credentials absent=true");
    }

    [Theory]
    [InlineData("")]
    [InlineData("za_bad")]
    [InlineData("zr_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("za_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    public async Task Noncanonical_access_is_invalid_without_database_access(string token)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine);
        var service = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        await Failure(AccountFailure.InvalidSession, () => service.AuthenticateAsync(token));
    }

    [Fact]
    public void Public_authentication_result_cannot_be_constructed_or_mutated_by_callers()
    {
        Assert.Empty(typeof(AccountAuthentication).GetConstructors());
        Assert.All(typeof(AccountAuthentication).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Equal(new[] { "AuthenticatedAt", "CredentialVersion", "FamilyId", "User" },
            typeof(AccountAuthentication).GetProperties().Select(x => x.Name).Order().ToArray());
    }

    [Fact]
    public async Task Actual_database_authentication_failure_is_safe_unavailable_not_unauthorized()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"))
            { Password = "synthetic-invalid-database-password" };
        var config = AccountsConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Schema, ["ConnectionStrings:Accounts"] = builder.ConnectionString
        }).Build());
        await using var source = config.CreateDataSource();
        var service = new AccountService(source, config, new AccountClock());
        await Failure(AccountFailure.DbUnavailable, () => service.AuthenticateAsync("za_" + new string('A', 43)));
        await Failure(AccountFailure.DbUnavailable, () => service.LoginAsync(Login()));
    }
}
