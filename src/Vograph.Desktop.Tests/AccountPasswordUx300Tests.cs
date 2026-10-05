using Vograph.Desktop.Features.Account;
using Xunit;

namespace Vograph.Desktop.Tests;

public class AccountPasswordUx300Tests
{
    [Fact]
    public void New_password_must_differ_from_current_before_request()
    {
        Assert.True(AccountPasswordRules.Same("Secret123456!", "Secret123456!"));
        Assert.False(AccountPasswordRules.Same("Secret123456!", "Secret123456?"));
        Assert.False(AccountPasswordRules.Same("", ""));
    }
}
