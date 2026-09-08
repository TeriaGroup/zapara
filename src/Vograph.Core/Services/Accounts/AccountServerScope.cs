using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Vograph.Core.Services.Accounts;

/// <summary>Explicit application root; its origin AND path isolate credentials.</summary>
public sealed class AccountServerScope
{
    public Uri BaseUri { get; }
    public string Key { get; }

    public AccountServerScope(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var loopback = uri.IsAbsoluteUri && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)));
        if (!uri.IsAbsoluteUri || (uri.Scheme != "https" && !(uri.Scheme == "http" && loopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Некорректный адрес API.");
        BaseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BaseUri.AbsoluteUri)));
    }

    public override string ToString() => "AccountServerScope";
}
