using Xunit;

namespace Zapara.Server.Tests;

[CollectionDefinition("Postgres fixture port", DisableParallelization = true)]
public sealed class PostgresFixturePortCollection;

[Collection("Postgres fixture port")]
public sealed class TestPostgresPortTests
{
    [Theory]
    [InlineData(null, 56432)]
    [InlineData("56432", 56432)]
    [InlineData("56543", 56543)]
    public void Approved_port_override_is_accepted_only_for_the_local_test_database(string? setting, int port)
    {
        WithPort(setting, () => AccountsPostgresFixture.ValidateConnection($"Host=127.0.0.1;Port={port};Database=zapara_test"));
    }

    [Theory]
    [InlineData("5432")]
    [InlineData("56544")]
    [InlineData("bad")]
    [InlineData(" ")]
    public void Unapproved_override_cannot_authorize_a_database(string setting)
    {
        WithPort(setting, () => Assert.Throws<InvalidOperationException>(() =>
            AccountsPostgresFixture.ValidateConnection("Host=127.0.0.1;Port=56432;Database=zapara_test")));
    }

    private static void WithPort(string? value, Action test)
    {
        var previous = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES_PORT");
        try { Environment.SetEnvironmentVariable("ZAPARA_TEST_POSTGRES_PORT", value); test(); }
        finally { Environment.SetEnvironmentVariable("ZAPARA_TEST_POSTGRES_PORT", previous); }
    }
}
