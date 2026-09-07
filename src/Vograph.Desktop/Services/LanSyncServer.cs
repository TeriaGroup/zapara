using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Vograph.Desktop.Services;

/// <summary>
/// LAN sync host: GET /sync/ answers the export JSON, POST /sync/ imports a payload. A minimal HTTP/1.1 server on
/// a raw TcpListener — HttpListener needs a URL reservation (netsh http add urlacl) or admin rights for
/// http://+:8765/, which made the switch fail for ordinary users (R44). Every Core call runs under the app-wide
/// gate; every connection is answered with Connection: close and dropped.
/// </summary>
public sealed class LanSyncServer : IDisposable
{
    /// <summary>A whole seeded profile exports to a couple of kilobytes; anything past this is refused unread.</summary>
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    /// <summary>Connections handled at once. One phone syncing needs one; a peer opening sockets it never finishes
    /// writing to would otherwise cost this process a handler, a deadline timer and a body buffer each. Past this,
    /// connections wait in the kernel's accept backlog, where they cost nothing managed at all.</summary>
    public const int MaxConnections = 8;
    private const int MaxHeadBytes = 16 * 1024;
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(10);

    private readonly AppServices _app;
    private readonly bool _localhostOnly;
    private readonly SemaphoreSlim _resolve = new(1, 1);
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _host;
    private int _pendingImports;

    /// <param name="port">8765 in the app; 0 asks the OS for a free port (tests), see <see cref="Port"/> after Start.</param>
    public LanSyncServer(AppServices app, int port = 8765, bool localhostOnly = false)
    {
        _app = app;
        Port = port;
        _localhostOnly = localhostOnly;
    }

    /// <summary>The port actually bound: after Start() with 0 this is the ephemeral one the OS handed out.</summary>
    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    /// <summary>«http://192.168.1.5:8765/sync/», empty until ResolveHostAsync has run once. Reading it never resolves anything.</summary>
    public string Address => _host is null ? "" : $"http://{_host}:{Port}/sync/";

    /// <summary>Bodies read off the wire that are waiting on, or running under, the Core gate. For tests.</summary>
    public int PendingImports => _pendingImports;

    /// <summary>Raised (on a pool thread) after a successful POST import.</summary>
    public event Action? Imported;

    /// <summary>This machine's LAN address (Core's DNS lookup, never on the UI thread), «localhost» for a loopback-only
    /// server. Resolved once per process under a lock, never throws — a failed lookup falls back to 127.0.0.1.</summary>
    public async Task<string> ResolveHostAsync()
    {
        if (_host is { } cached) return cached;
        await _resolve.WaitAsync();
        try
        {
            if (_host is { } again) return again;
            var host = "localhost";
            if (!_localhostOnly)
            {
                try { host = await Task.Run(() => _app.Sync.GetLocalIp()); }
                catch (Exception ex)
                {
                    _app.Log.Error("lan sync address", ex);
                    host = "127.0.0.1";
                }
            }
            return _host = host;
        }
        finally
        {
            _resolve.Release();
        }
    }

    public async Task<string> ResolveAddressAsync() => $"http://{await ResolveHostAsync()}:{Port}/sync/";

    /// <summary>The toast for a failed Start: a port another program owns gets its own message.</summary>
    public string StartFailureText(Exception ex) =>
        ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } ? _app.Loc.T("syncLanBusy", Port) : _app.Loc.T("syncLanFail", ex.Message);

    /// <summary>Throws SocketException when the port cannot be bound; nothing is left listening in that case.</summary>
    public void Start()
    {
        if (IsRunning) return;
        var listener = new TcpListener(_localhostOnly ? IPAddress.Loopback : IPAddress.Any, Port);
        try { listener.Start(); }
        catch
        {
            listener.Stop();
            throw;
        }
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _listener = listener;
        _app.Log.Info($"lan sync: listening on port {Port}");
        _ = AcceptLoopAsync(listener, _cts.Token);
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try { listener?.Stop(); }
        catch (SocketException ex) { _app.Log.Warn($"lan sync: {ex.Message}"); }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // The slot is taken *before* the accept, so a peer opening more sockets than this server will serve leaves
            // them queued in the kernel's backlog — unaccepted, unread and costing this process nothing — instead of
            // one handler each. Stop() cancels the token, which aborts the wait rather than deadlocking the loop.
            try { await _slots.WaitAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { break; }
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException)
            {
                _slots.Release();
                break;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                _slots.Release();
                if (!ct.IsCancellationRequested) _app.Log.Warn($"lan sync: accept failed: {ex.Message}");
                break;
            }
            _ = HandleAsync(client, ct); // gives the slot back in its own finally, whatever happens to the connection
        }
        _app.Log.Info("lan sync: stopped listening");
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
                io.CancelAfter(IoTimeout);
                var stream = client.GetStream();
                var request = await HttpHead.ReadAsync(stream, MaxHeadBytes, io.Token);
                if (request is null)
                {
                    await Respond(stream, 400, "{\"status\":\"error\"}", io.Token, drainRequest: true);
                    return;
                }
                if (!request.Target.Equals("/sync", StringComparison.OrdinalIgnoreCase) && !request.Target.StartsWith("/sync/", StringComparison.OrdinalIgnoreCase))
                {
                    await Respond(stream, 404, "{\"status\":\"error\"}", io.Token, drainRequest: true);
                    return;
                }
                switch (request.Method)
                {
                    case "GET":
                        await Respond(stream, 200, await GatedAsync(() => _app.Sync.ExportToJson(), io.Token), io.Token);
                        return;
                    case "POST":
                        await ImportAsync(stream, request, io.Token);
                        return;
                    default:
                        await Respond(stream, 405, "", io.Token, "Allow: GET, POST\r\n", drainRequest: true);
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            _app.Log.Warn($"lan sync: connection dropped: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _app.Log.Error("lan sync", ex);
        }
        finally
        {
            _slots.Release(); // one accepted client, one slot — released here even when the handler threw or timed out
        }
    }

    private async Task ImportAsync(NetworkStream stream, HttpHead request, CancellationToken ct)
    {
        // A peer on the LAN is not trusted with the process's memory: no declared length (chunked) means no bound to
        // check, one past the cap is refused before a single body byte is read, and the read stops at the length.
        if (request.Chunked || request.ContentLength is null)
        {
            _app.Log.Warn("lan sync: refused a body without Content-Length");
            await Respond(stream, 411, "{\"status\":\"error\"}", ct, drainRequest: true);
            return;
        }
        if (request.ContentLength > MaxBodyBytes)
        {
            _app.Log.Warn($"lan sync: refused a body of {request.ContentLength} bytes (limit {MaxBodyBytes})");
            await Respond(stream, 413, "{\"status\":\"error\"}", ct, drainRequest: true);
            return;
        }
        var body = await request.ReadBodyAsync(stream, ct);
        // Only the import itself is answered with 400: the phone may well drop off the Wi-Fi between the commit and
        // the answer, and a failed write must not be reported as a failed import — the data is in the database by
        // then and the shell has to hear about it. Imported subscribers marshal to the UI thread themselves, so the
        // event goes out before the response; a write that fails after it is logged as a dropped connection.
        string counts;
        Interlocked.Increment(ref _pendingImports);
        try
        {
            counts = await GatedAsync(() =>
            {
                var (o, h, f) = _app.Sync.ImportFromJson(body);
                return $"{o}/{h}/{f}";
            }, ct);
        }
        catch (Exception ex)
        {
            _app.Log.Error("lan sync import", ex);
            await Respond(stream, 400, "{\"status\":\"error\"}", ct);
            return;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingImports);
        }
        _app.Log.Info($"lan sync: imported {counts} (overrides/homework/friends)");
        Imported?.Invoke();
        await Respond(stream, 200, "{\"status\":\"ok\"}", ct);
    }

    /// <param name="drainRequest">For a refusal, whose request body was never read: see <see cref="DrainAsync"/>.</param>
    private static async Task Respond(NetworkStream stream, int status, string body, CancellationToken ct, string extraHeaders = "", bool drainRequest = false)
    {
        var reason = status switch
        {
            200 => "OK", 400 => "Bad Request", 404 => "Not Found", 405 => "Method Not Allowed",
            411 => "Length Required", 413 => "Payload Too Large", _ => "Error"
        };
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n{extraHeaders}\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        stream.Socket.Shutdown(SocketShutdown.Send);
        if (drainRequest) await DrainAsync(stream, ct);
    }

    /// <summary>
    /// A lingering close after a refusal. The peer is likely still writing the body this answer refused, and closing
    /// a socket that still holds unread bytes makes Windows send RST — which throws away the answer already sitting
    /// in the peer's own receive buffer, so a client posting past the cap sees «connection reset» instead of the 413.
    /// The rest is therefore read and thrown away — one small buffer, nothing kept, so the cap still bounds the
    /// memory a LAN peer can make this process allocate — until the peer's FIN arrives, a cap's worth of bytes has
    /// been dropped, or the request's I/O deadline expires (the send side is already shut down, so no reply follows).
    /// </summary>
    private static async Task DrainAsync(NetworkStream stream, CancellationToken ct)
    {
        var sink = new byte[8 * 1024];
        var dropped = 0;
        while (dropped <= MaxBodyBytes)
        {
            var n = await stream.ReadAsync(sink, ct);
            if (n == 0) return;
            dropped += n;
        }
    }

    /// <summary>The handler's own deadline covers the wait for the gate too: queued behind a long Core call, a
    /// request that has already outlived its 10 s gives up here instead of running an import for a socket nobody
    /// is listening on any more. A cancelled wait never acquired the gate, so it must not release it either.</summary>
    private async Task<T> GatedAsync<T>(Func<T> work, CancellationToken ct)
    {
        await _app.CoreGate.WaitAsync(ct);
        try { return await Task.Run(work); }
        finally { _app.CoreGate.Release(); }
    }

    public void Dispose() => Stop();
}

/// <summary>Request line and headers of one HTTP/1.1 request, read up to the blank line (capped). Body bytes that
/// arrived in the same read are kept and handed back by ReadBodyAsync.</summary>
internal sealed class HttpHead
{
    private static readonly char[] QueryOrFragment = new[] { '?', '#' };

    private readonly byte[] _leftover;

    private HttpHead(string method, string target, long? contentLength, bool chunked, byte[] leftover)
    {
        Method = method;
        Target = target;
        ContentLength = contentLength;
        Chunked = chunked;
        _leftover = leftover;
    }

    public string Method { get; }
    public string Target { get; }
    public long? ContentLength { get; }
    public bool Chunked { get; }

    /// <summary>Null for a head that is not HTTP, exceeds the cap, or ends before the blank line.</summary>
    public static async Task<HttpHead?> ReadAsync(Stream stream, int maxHeadBytes, CancellationToken ct)
    {
        var buffer = new byte[maxHeadBytes];
        var read = 0;
        int end;
        while ((end = IndexOfBlankLine(buffer, read)) < 0)
        {
            if (read >= buffer.Length) return null;
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0) return null;
            read += n;
        }
        var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) return null;
        var target = NormalizeTarget(parts[1]);
        if (target is null) return null;
        long? length = null;
        var chunked = false;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                // RFC 7230 §3.3.3: a length that is not a non-negative number, and two lengths that disagree, are
                // both 400. Taking the last one would let a peer that also talks to a proxy have the two of us read
                // a different number of body bytes off the same connection.
                if (!long.TryParse(value, out var l) || l < 0) return null;
                if (length is { } seen && seen != l) return null;
                length = l;
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase)) chunked = true;
        }
        var bodyStart = end + 4;
        return new HttpHead(parts[0].ToUpperInvariant(), target, length, chunked, buffer[bodyStart..read]);
    }

    /// <summary>The request target reduced to the path the server routes on. RFC 7230 §5.3.2 obliges an origin
    /// server to accept the absolute form as well («GET http://192.168.1.5:8765/sync/ HTTP/1.1», which is what a
    /// client behind a proxy sends), and a query or fragment is no part of the route. Null for anything that is not
    /// a path after that — the caller answers 400.</summary>
    private static string? NormalizeTarget(string raw)
    {
        var target = raw;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var slash = target.IndexOf('/', target.IndexOf("//", StringComparison.Ordinal) + 2); // the third slash: the end of the authority
            target = slash < 0 ? "/" : target[slash..];
        }
        var cut = target.IndexOfAny(QueryOrFragment);
        if (cut >= 0) target = target[..cut];
        return target.StartsWith('/') ? target : null;
    }

    /// <summary>Exactly Content-Length bytes (the caller checked it against the cap), decoded as UTF-8. The buffer
    /// grows with the bytes that actually arrive, 8 KB at a time: allocating the declared length up front would let
    /// a peer make this process reserve a cap's worth of heap per connection for nothing but a header line.</summary>
    public async Task<string> ReadBodyAsync(Stream stream, CancellationToken ct)
    {
        var total = (int)(ContentLength ?? 0);
        var have = Math.Min(_leftover.Length, total);
        using var body = new MemoryStream(have);
        body.Write(_leftover, 0, have);
        if (have < total)
        {
            var chunk = new byte[Math.Min(8 * 1024, total - have)];
            while (have < total)
            {
                var n = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, total - have)), ct);
                if (n == 0) throw new IOException("client closed the connection before the declared body arrived");
                body.Write(chunk, 0, n);
                have += n;
            }
        }
        return Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
    }

    private static int IndexOfBlankLine(byte[] buffer, int length)
    {
        for (var i = 3; i < length; i++)
            if (buffer[i - 3] == (byte)'\r' && buffer[i - 2] == (byte)'\n' && buffer[i - 1] == (byte)'\r' && buffer[i] == (byte)'\n') return i - 3;
        return -1;
    }
}
