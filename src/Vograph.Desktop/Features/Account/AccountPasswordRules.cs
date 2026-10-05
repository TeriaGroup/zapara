namespace Vograph.Desktop.Features.Account;

public static class AccountPasswordRules
{
    public static bool Same(string current, string next) => current.Length > 0 &&
        string.Equals(current, next, StringComparison.Ordinal);
}
