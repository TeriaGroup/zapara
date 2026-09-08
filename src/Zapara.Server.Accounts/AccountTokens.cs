using System.Security.Cryptography;
using System.Text;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

internal static class AccountTokens
{
    internal static string Create(string prefix) => prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Hash(string token, string prefix)
    {
        try { AccountValidation.Token(token, prefix); }
        catch (ArgumentException) { throw new AccountServiceException(AccountFailure.InvalidSession); }
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
