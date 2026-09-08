using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class SyncSurfaceTests
{
    [Fact]
    public void Typed_mutation_contract_is_present()
        => Assert.NotNull(typeof(UserResponse).Assembly.GetType("Zapara.Contracts.Sync.SyncMutation"));

    [Fact]
    public void Trusted_bridge_is_public_without_public_context_constructor()
    {
        var boundary = typeof(AccountService).Assembly.GetType("Zapara.Server.Accounts.IAccountUnitOfWork");
        Assert.NotNull(boundary);
        Assert.True(boundary.IsPublic);
        Assert.True(boundary.IsAssignableFrom(typeof(AccountService)));
        var context = typeof(AccountService).Assembly.GetType("Zapara.Server.Accounts.TrustedAccountContext");
        Assert.NotNull(context);
        Assert.True(context.IsSealed);
        Assert.Empty(context.GetConstructors());
        Assert.Equal(new[] { "Connection", "FamilyId", "Transaction", "UserId", "UtcNow" },
            context.GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Migration_module_is_present_with_real_accounts_baseline()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true, targetVersion: 1);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Zapara.Server.Sync.dll")),
            "Separate Sync migration assembly must be built and referenced.");
    }
}
