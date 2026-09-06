using System.Net;
using System.Text;

namespace Vograph.Desktop.Services;

/// <summary>
/// The same protocol as Core's SyncService.SyncHost (GET /sync/ → export JSON, POST /sync/ → import), but every
/// Core call runs under the app-wide gate — Core's host reads and writes SQLite from the listener thread.
/// </summary>
public sealed class LanSyncServer : IDisposable
{
    /// <summary>A whole seeded profile exports to a couple of kilobytes; anything past this is refused unread.</summary>
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    private readonly AppServices _app;
    private readonly bool _localhostOnly;
    private HttpListener? _listener;
    private string? _address;

    public LanSyncServer(AppServices app, int port = 8765, bool localhostOnly = false)
    {
        _app = app;
        Port = port;
        _localhostOnly = localhostOnly;
    }

    public int Port { get; }
    public bool IsRunning => _listener is { IsListening: true };

    /// <summary>
    /// The cached published address («http://192.168.1.5:8765/sync/»), empty until <see cref="ResolveAddressAsync"/>
    /// has produced it once. Reading it never resolves anything: the host name comes from a DNS lookup
    /// (Core's <c>SyncService.GetLocalIp</c>) that must never run on the UI thread, so callers on that thread read
    /// this property and nothing else.
    /// </summary>
    public string Address => _address ?? "";

    /// <summary>
    /// Fills <see cref="Address"/> off the UI thread and returns it. Resolves at most once per process (the machine's
    /// own address does not change while it runs) and never throws — a failed lookup falls back to the loopback
    /// address, exactly like Core does.
    /// </summary>
    public async Task<string> ResolveAddressAsync()
    {
        if (_address is { } cached) return cached;
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
        return _address = $"http://{host}:{Port}/sync/";
    }

    /// <summary>
    /// The toast for a failed <see cref="Start"/>. Windows answers a prefix with no URL reservation with access
    /// denied (HttpListenerException 5), which the user can only fix with admin rights or `netsh http add urlacl` —
    /// a different message from «the port is taken».
    /// </summary>
    public string StartFailureText(Exception ex) =>
        ex is HttpListenerException { ErrorCode: 5 } ? _app.Loc.T("syncLanAcl") : _app.Loc.T("syncLanFail", ex.Message);

    /// <summary>Raised (on a pool thread) after a successful POST import.</summary>
    public event Action? Imported;

    /// <summary>Throws HttpListenerException when the prefix cannot be bound (no URL ACL for http://+ without admin rights).</summary>
    public void Start()
    {
        if (IsRunning) return;
        var listener = new HttpListener();
        listener.Prefixes.Add(_localhostOnly ? $"http://localhost:{Port}/sync/" : $"http://+:{Port}/sync/");
        listener.Start();
        _listener = listener;
        _app.Log.Info($"lan sync: listening on port {Port}"); // the address follows from ResolveAddressAsync
        _ = LoopAsync(listener);
    }

    public void Stop()
    {
        var l = _listener;
        _listener = null;
        try { l?.Stop(); l?.Close(); }
        catch (ObjectDisposedException ex) { _app.Log.Warn($"lan sync: {ex.Message}"); }
    }

    private async Task LoopAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                _app.Log.Info($"lan sync: stopped listening ({ex.GetType().Name})");
                return;
            }
            _ = HandleAsync(ctx);
        }
        _app.Log.Info("lan sync: stopped listening");
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.HttpMethod == "GET")
            {
                var json = await GatedAsync(() => _app.Sync.ExportToJson());
                await Write(ctx.Response, 200, json, "application/json");
            }
            else if (ctx.Request.HttpMethod == "POST")
            {
                // A peer on the LAN is not trusted with the process's memory: an undeclared length (chunked) or one
                // past the cap is refused before a single byte is read, and the read itself stops at that length.
                var declared = ctx.Request.ContentLength64;
                if (declared < 0 || declared > MaxBodyBytes)
                {
                    _app.Log.Warn($"lan sync: refused a body of {declared} bytes (limit {MaxBodyBytes})");
                    await Write(ctx.Response, 413, "{\"status\":\"error\"}", "application/json");
                    return;
                }
                var body = await ReadBodyAsync(ctx.Request, (int)declared);
                if (body is null)
                {
                    _app.Log.Warn($"lan sync: refused a body longer than its declared {declared} bytes");
                    await Write(ctx.Response, 413, "{\"status\":\"error\"}", "application/json");
                    return;
                }
                try
                {
                    var counts = await GatedAsync(() =>
                    {
                        var (o, h, f) = _app.Sync.ImportFromJson(body);
                        return $"{o}/{h}/{f}";
                    });
                    await Write(ctx.Response, 200, "{\"status\":\"ok\"}", "application/json");
                    _app.Log.Info($"lan sync: imported {counts} (overrides/homework/friends)");
                    Imported?.Invoke();
                }
                catch (Exception ex)
                {
                    _app.Log.Error("lan sync import", ex);
                    await Write(ctx.Response, 400, "{\"status\":\"error\"}", "application/json");
                }
            }
            else await Write(ctx.Response, 405, "", "text/plain");
        }
        catch (Exception ex)
        {
            _app.Log.Error("lan sync", ex);
            try { ctx.Response.Abort(); }
            catch (ObjectDisposedException disposed) { _app.Log.Warn($"lan sync: {disposed.Message}"); }
        }
    }

    /// <summary>
    /// Reads the request body through a buffer capped at the declared length, plus one byte to notice a peer that
    /// sends more than it declared — in that case null comes back and the caller answers 413.
    /// </summary>
    private static async Task<string?> ReadBodyAsync(HttpListenerRequest request, int declared)
    {
        var buffer = new byte[declared + 1];
        var read = 0;
        while (read <= declared)
        {
            var n = await request.InputStream.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
            if (n == 0) break;
            read += n;
        }
        return read > declared ? null : (request.ContentEncoding ?? Encoding.UTF8).GetString(buffer, 0, read);
    }

    private static async Task Write(HttpListenerResponse resp, int status, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        resp.StatusCode = status;
        resp.ContentType = contentType;
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    private async Task<T> GatedAsync<T>(Func<T> work)
    {
        await _app.CoreGate.WaitAsync();
        try { return await Task.Run(work); }
        finally { _app.CoreGate.Release(); }
    }

    public void Dispose() => Stop();
}
