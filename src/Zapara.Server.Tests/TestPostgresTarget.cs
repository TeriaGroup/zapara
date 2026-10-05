namespace Zapara.Server.Tests;

internal static class TestPostgresTarget
{
    internal static int Port => Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES_PORT") switch
    {
        null or "56432" => 56432,
        "56543" => 56543,
        _ => throw new InvalidOperationException("Only an approved local fixture port is allowed.")
    };
}
