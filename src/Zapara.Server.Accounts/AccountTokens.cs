using System.Security.Cryptography;
using System.Text;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

internal static class AccountTokens
{
    internal static string Create(string prefix) => prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string DeriveReplacement(string refreshToken, Guid attemptId, string prefix)
    {
        if (attemptId == Guid.Empty || prefix is not ("za_" or "zr_"))
            throw new AccountServiceException(AccountFailure.InvalidRequest);
        Hash(refreshToken, "zr_");
        // A retry can reproduce its own token pair without persisting plaintext tokens.
        // The attempt ID is random, stable across retries, and domain-separated per token kind.
        var purpose = Encoding.UTF8.GetBytes("zapara:refresh-v2:" + prefix + ":" + attemptId.ToString("D"));
        var value = HMACSHA256.HashData(Encoding.UTF8.GetBytes(refreshToken), purpose);
        return prefix + Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static byte[] Hash(string token, string prefix)
    {
        try { AccountValidation.Token(token, prefix); }
        catch (ArgumentException) { throw new AccountServiceException(AccountFailure.InvalidSession); }
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
