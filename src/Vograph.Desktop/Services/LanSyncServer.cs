using System.Net;
using System.Text;

namespace Vograph.Desktop.Services;

/// <summary>
/// The same protocol as Core's SyncService.SyncHost (GET /sync/ → export JSON, POST /sync/ → import), but every
/// Core call runs under the app-wide gate — Core's host reads and writes SQLite from the listener thread.
/// </summary>
public sealed class LanSyncServer : IDisposable
{
    private readonly AppServices _app;
    private readonly bool _localhostOnly;
    private HttpListener? _listener;

    public LanSyncServer(AppServices app, int port = 8765, bool localhostOnly = false)
    {
        _app = app;
        Port = port;
        _localhostOnly = localhostOnly;
    }

    public int Port { get; }
    public bool IsRunning => _listener is { IsListening: true };
    public string Address => $"http://{(_localhostOnly ? "localhost" : _app.Sync.GetLocalIp())}:{Port}/sync/";

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
        _app.Log.Info($"lan sync: listening on port {Port}"); // not Address: that one resolves the local IP
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
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
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
