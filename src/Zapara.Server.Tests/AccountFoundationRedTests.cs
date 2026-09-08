using System.Reflection;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class AccountFoundationRedTests
{
    [Theory]
    [InlineData("Zapara.Contracts", "Zapara.Contracts.Accounts.RegisterRequest")]
    [InlineData("Zapara.Server.Accounts", "Zapara.Server.Accounts.AccountsConfiguration")]
    [InlineData("Zapara.Server.Accounts", "Zapara.Server.Accounts.AccountsMigrations")]
    public void FoundationBoundaryExists(string assembly, string name)
        => Assert.NotNull(Assembly.Load(assembly).GetType(name));
}
