using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed partial class AccountServiceTests
{
    [Fact]
    public async Task Runtime_facade_exists_without_initializing_database()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine);
        var type = typeof(AccountsDataSource).Assembly.GetType("Zapara.Server.Accounts.AccountService");
        Assert.NotNull(type);
        _ = new AccountService(db.DataSource, db.Configuration, new AccountClock());
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}'"));
    }

    [Theory]
    [InlineData(AccountFailure.InvalidCredentials, "invalid_credentials")]
    [InlineData(AccountFailure.InvalidSession, "invalid_session")]
    [InlineData(AccountFailure.UsernameUnavailable, "username_unavailable")]
    [InlineData(AccountFailure.SessionNotFound, "session_not_found")]
    [InlineData(AccountFailure.InvalidRequest, "invalid_request")]
    [InlineData(AccountFailure.RateLimited, "rate_limited")]
    [InlineData(AccountFailure.DbUnavailable, "db_unavailable")]
    public void Safe_failure_has_stable_code(AccountFailure failure, string expected)
    {
        var error = new AccountServiceException(failure);
        var property = error.GetType().GetProperty("Code");
        Assert.NotNull(property);
        Assert.Equal(expected, property.GetValue(error));
        Assert.Null(error.InnerException);
    }
}
