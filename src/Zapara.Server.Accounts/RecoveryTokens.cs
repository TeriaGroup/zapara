using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Zapara.Server.Accounts;

internal static class RecoveryTokens
{
    internal static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    internal static byte[] Hash(string? token)
    {
        try { ExternalSecrets.Token(token, 43, 43); }
        catch (ExternalAuthException) { throw new AccountServiceException(AccountFailure.InvalidRequest); }
        return SHA256.HashData(Encoding.UTF8.GetBytes(token!));
    }
}
