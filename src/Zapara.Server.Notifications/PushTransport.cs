using System.Net;
using System.Net.Sockets;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace Zapara.Server.Notifications;

public enum PushDeliveryOutcome { Accepted, Gone, Unavailable }
public interface IPushTransport
{
    Task<PushDeliveryOutcome> SendAsync(PushSubscriptionRequest subscription, CancellationToken ct);
}

public sealed class PushTransport : IPushTransport, IDisposable
{
    public const string NeutralPayload = "{\"title\":\"Запара\",\"body\":\"Откройте приложение, чтобы проверить расписание и задания.\",\"url\":\"/app/\",\"tag\":\"zapara-reminder\"}";
    private readonly HttpClient http;
    private readonly VapidAuthentication authentication;
    private readonly PushServiceClient client;
    public PushTransport(PushConfiguration configuration, HttpClient? controlledClient = null)
    {
        if (!configuration.Available) throw new PushOperationException(503, "push_unavailable");
        authentication = new(configuration.PublicKey!, configuration.PrivateKey!) { Subject = configuration.Subject, Expiration = 3600 };
        http = controlledClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            ConnectCallback = ConnectPublicAsync, ConnectTimeout = TimeSpan.FromSeconds(10)
        });
        client = new(http) { DefaultAuthentication = authentication, AutoRetryAfter = false, DefaultTimeToLive = 300 };
    }
    public async Task<PushDeliveryOutcome> SendAsync(PushSubscriptionRequest subscription, CancellationToken ct)
    {
        PushValidation.Subscription(subscription);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var target = new PushSubscription { Endpoint = subscription.Endpoint, Keys = new Dictionary<string, string>
        { ["p256dh"] = subscription.Keys.P256dh, ["auth"] = subscription.Keys.Auth } };
        try
        {
            await client.RequestPushMessageDeliveryAsync(target, new PushMessage(NeutralPayload) { TimeToLive = 300, Topic = "zapara-reminder" }, deadline.Token);
            return PushDeliveryOutcome.Accepted;
        }
        catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound) { return PushDeliveryOutcome.Gone; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is PushServiceClientException or HttpRequestException or OperationCanceledException or IOException)
        { return PushDeliveryOutcome.Unavailable; }
    }
    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        if (!PushValidation.AllowedHost(context.DnsEndPoint.Host) || context.DnsEndPoint.Port != 443) throw new HttpRequestException("Недопустимый push-сервис.");
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(address => !PublicAddress(address))) throw new HttpRequestException("Недопустимый адрес push-сервиса.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(new IPEndPoint(address, 443), ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); }
        }
        throw new HttpRequestException("Push-сервис недоступен.");
    }
    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4) return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
            && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && bytes[1] == 168) && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
            && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
            && !(bytes[0] == 198 && (bytes[1] is 18 or 19 || (bytes[1] == 51 && bytes[2] == 100)))
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        return bytes.Length == 16 && (bytes[0] & 0xe0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 &&
                ((bytes[2] == 0x0d && bytes[3] == 0xb8) || (bytes[2] == 0 && bytes[3] is 0 or 2 or 0x10 or 0x20)));
    }
    public void Dispose() { authentication.Dispose(); http.Dispose(); }
}
