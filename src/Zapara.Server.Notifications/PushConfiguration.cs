using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Zapara.Server.Sync;

namespace Zapara.Server.Notifications;

public sealed class PushConfiguration
{
    public bool Available { get; }
    public string? PublicKey { get; }
    internal string? PrivateKey { get; }
    internal string? Subject { get; }
    public string? Reason { get; }
    private PushConfiguration(bool available, string? publicKey, string? privateKey, string? subject, string? reason)
        => (Available, PublicKey, PrivateKey, Subject, Reason) = (available, publicKey, privateKey, subject, reason);
    public static PushConfiguration Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Web:Push");
        if (!bool.TryParse(section["Enabled"], out var enabled) || !enabled) return new(false, null, null, null, "not_configured");
        if (!SyncConfiguration.IsEnabled(configuration)) return new(false, null, null, null, "sync_unavailable");
        try
        {
            var pub = PushValidation.Key(section["PublicKey"], 65);
            var key = PushValidation.Key(section["PrivateKey"], 32);
            var subject = section["Subject"];
            if (subject is null || subject.Length > 254 || subject.Any(c => char.IsControl(c) || c is '"' or '\\')
                || !Uri.TryCreate(subject, UriKind.Absolute, out var uri) || uri.Scheme is not ("mailto" or "https")) throw new ArgumentException();
            using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = key });
            var point = ec.ExportParameters(false).Q;
            if (pub[0] != 4 || !pub.AsSpan(1, 32).SequenceEqual(point.X) || !pub.AsSpan(33, 32).SequenceEqual(point.Y)) throw new ArgumentException();
            return new(true, section["PublicKey"], section["PrivateKey"], subject, null);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or CryptographicException)
        { return new(false, null, null, null, "invalid_configuration"); }
    }
    public override string ToString() => "PushConfiguration { [REDACTED] }";
}

public sealed record PushKeys(string P256dh, string Auth)
{
    public override string ToString() => "PushKeys { [REDACTED] }";
}
public sealed record PushSubscriptionRequest(string Endpoint, PushKeys Keys, bool Enabled = true, string TimeZone = "Europe/Moscow")
{
    public override string ToString() => "PushSubscriptionRequest { [REDACTED] }";
}
public sealed record PushSubscriptionResponse(Guid SubscriptionId, bool Enabled, string TimeZone, IReadOnlyList<string?> Times);
public sealed record PushTestRequest(Guid SubscriptionId);
public sealed class PushOperationException(int status, string code) : Exception("Операция уведомлений отклонена.")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class PushValidation
{
    internal static byte[] Key(string? value, int bytes)
    {
        if (value is null || value.Length > 128 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new ArgumentException();
        var decoded = WebEncoders.Base64UrlDecode(value);
        if (decoded.Length != bytes || WebEncoders.Base64UrlEncode(decoded) != value) throw new ArgumentException();
        return decoded;
    }
    public static Uri Endpoint(string? value)
    {
        if (value is null || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host.EndsWith('.')
            || uri.AbsolutePath.Length < 2 || !AllowedHost(uri.IdnHost)) throw new PushOperationException(400, "invalid_subscription");
        return uri;
    }
    public static bool AllowedHost(string host) => host is "fcm.googleapis.com" or "web.push.apple.com" or "updates.push.services.mozilla.com"
        || (host.EndsWith(".push.apple.com", StringComparison.Ordinal) && host.Length > ".push.apple.com".Length)
        || (host.EndsWith(".notify.windows.com", StringComparison.Ordinal) && host.Length > ".notify.windows.com".Length);
    public static void Subscription(PushSubscriptionRequest request)
    {
        try
        {
            Endpoint(request.Endpoint);
            if (request.Keys is null) throw new ArgumentException();
            var pub = Key(request.Keys.P256dh, 65);
            _ = Key(request.Keys.Auth, 16);
            if (pub[0] != 4) throw new ArgumentException();
            using var ec = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = pub[1..33], Y = pub[33..65] } });
            if (request.TimeZone is null || request.TimeZone.Length > 80) throw new ArgumentException();
            _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or CryptographicException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new PushOperationException(400, "invalid_subscription"); }
    }
}
