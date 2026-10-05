using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Zapara.Server.Admin;

internal static class AdminTokens
{
    internal const string Prefix = "zw_";

    internal static string Create()
        => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Hash(string token)
    {
        if (token is null || token.Length != 46 || !token.StartsWith(Prefix, StringComparison.Ordinal)) throw AdminException.Unauthorized();
        var encoded = token[3..];
        if (!Regex.IsMatch(encoded, @"\A[A-Za-z0-9_-]{43}\z", RegexOptions.CultureInvariant)) throw AdminException.Unauthorized();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/') + "="); }
        catch (FormatException) { throw AdminException.Unauthorized(); }
        if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != encoded) throw AdminException.Unauthorized();
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
