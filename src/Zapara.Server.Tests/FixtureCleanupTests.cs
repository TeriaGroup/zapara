using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed class FixtureCleanupTests
{
    [Fact]
    public async Task Account_fixture_removes_the_social_schema_created_for_its_host()
    {
        var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var social = db.Schema + "_social";
        await using var observer = new NpgsqlConnection(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"));
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            await using (var host = new WebAccountHost(db))
                Assert.Equal(social, host.Factory.Services.GetRequiredService<SocialConfiguration>().Schema);
            await db.DisposeAsync();
            await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@name", observer);
            check.Parameters.AddWithValue("name", social);
            Assert.Equal(0L, await check.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await db.DisposeAsync();
            // Only the sibling of this generated fixture; also cleans up the RED run.
            await using var clean = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{social}\" CASCADE", observer);
            await clean.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
