using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Vograph.Desktop.Services.Accounts;

/// <summary>Owns the bound socket for one native OAuth attempt. No URL reservations are required.</summary>
internal sealed class ExternalLoopback : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    public int Port { get; }

    public ExternalLoopback()
    {
        listener.Server.ExclusiveAddressUse = true;
        listener.Start(8);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task<string> ReceiveAsync(Guid transaction, CancellationToken ct)
    {
        while (true)
        {
            using var connection = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var stream = connection.GetStream();
                var bytes = new byte[8192];
                var used = 0;
                var end = -1;
                while (used < bytes.Length && end < 0)
                {
                    var count = await stream.ReadAsync(bytes.AsMemory(used), deadline.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    used += count;
                    end = Encoding.ASCII.GetString(bytes, 0, used).IndexOf("\r\n\r\n", StringComparison.Ordinal);
                }
                var code = end < 0 ? null : Parse(Encoding.ASCII.GetString(bytes, 0, end), transaction);
                var body = code is null ? "Некорректный ответ входа." : "Вернитесь в приложение для завершения входа.";
                var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {(code is null ? "400 Bad Request" : "200 OK")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n{body}");
                try { await stream.WriteAsync(response, deadline.Token).ConfigureAwait(false); }
                catch (IOException) { /* A closed browser tab does not invalidate an accepted handoff. */ }
                if (code is not null) return code;
            }
            catch (IOException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
    }

    private string? Parse(string headers, Guid expected)
    {
        var lines = headers.Split("\r\n", StringSplitOptions.None);
        var request = lines[0].Split(' ');
        if (request.Length != 3 || request[0] != "GET" || request[2] != "HTTP/1.1") return null;
        var hosts = lines.Skip(1).Where(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hosts.Length != 1 || hosts[0][5..].Trim() != $"127.0.0.1:{Port}") return null;
        if (lines.Skip(1).Any(l => l.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))) return null;
        const string prefix = "/zapara/oauth/callback?";
        if (!request[1].StartsWith(prefix, StringComparison.Ordinal) || request[1].Contains('#')) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in request[1][prefix.Length..].Split('&'))
        {
            var fields = pair.Split('=');
            // Contract values are GUID/base64url, so no escaped or ambiguous forms are necessary.
            if (fields.Length != 2 || !values.TryAdd(fields[0], fields[1])) return null;
        }
        if (values.Count != 2 || !values.TryGetValue("transactionId", out var id)
            || !Guid.TryParseExact(id, "D", out var parsed) || parsed != expected
            || !values.TryGetValue("handoffCode", out var code) || code.Length != 43
            || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        return code;
    }

    public void Dispose() => listener.Stop();
}
