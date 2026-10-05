using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

internal sealed class AccountClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class AccountTestSupport
{
    internal const string Password = "correct test password 🌍";
    internal const string NewPassword = "replacement password 🌏";
    internal static LoginRequest Login(string username = "test.user", string password = Password, Guid? device = null)
        => new(username, password, new(device ?? Guid.NewGuid(), "Тестовое устройство", "windows"));
    internal static async Task<SessionResponse> Seed(AccountService service, string username = "test.user")
    {
        var user = await service.RegisterAsync(new(username, Password));
        Assert.NotNull(user);
        var session = await service.LoginAsync(Login(username));
        Assert.NotNull(session);
        return session;
    }
    internal static async Task Failure(AccountFailure expected, Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<AccountServiceException>(operation);
        Assert.Equal(expected, error.Failure);
        Assert.Null(error.InnerException);
    }
}
