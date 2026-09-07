using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SyncTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 15, 0, 0);

    /// <summary>Never the production server (every interface on 8765): loopback only, and port 0 asks the OS for a
    /// free one — 8765 may well be taken on the machine running the suite.</summary>
    private static LanSyncServer Loopback(AppServices services, int port = 0) => new(services, port, localhostOnly: true);

    /// <summary>One raw HTTP/1.1 exchange over a fresh TcpClient (no HttpClient normalisation in the way): returns the status line.</summary>
    private static async Task<string> RawStatusAsync(int port, string request, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), ct);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return (await reader.ReadLineAsync(ct)) ?? "";
    }

    [Fact]
    public async Task Export_And_Import_Through_File_Dialogs()
    {
        using var source = TestDb.Create();
        using var target = TestDb.Create(seedPersonalization: false);
        var savePath = Path.Combine(source.Dir, "sync.json");
        var dialogs = new FakeFileDialogs { SavePath = savePath };
        source.Services.FileDialogs = dialogs;
        var shell = new ShellViewModel(source.Services);
        var vm = new SettingsViewModel(source.Services, shell, () => Sun6);

        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal("vograph-sync-20260906.json", dialogs.LastSuggestedName);
        // Core's serializer escapes Cyrillic (М…), so the payload is read back rather than grepped.
        var exported = JsonSerializer.Deserialize<SyncService.SyncPayload>(File.ReadAllText(savePath));
        Assert.NotNull(exported);
        Assert.Equal("Матан", Assert.Single(exported.Overrides).DisplayName);
        Assert.Contains(source.Services.Toasts.Items, t => t.Text.StartsWith("Экспорт сохранён"));

        target.Services.FileDialogs = new FakeFileDialogs { OpenPath = savePath };
        var targetShell = new ShellViewModel(target.Services);
        var changed = 0;
        targetShell.ScheduleChanged += () => changed++;
        var targetVm = new SettingsViewModel(target.Services, targetShell, () => Sun6);
        await targetVm.ImportCommand.ExecuteAsync(null);
        Assert.Equal("Матан", target.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1));
        Assert.Single(target.Services.Db.GetFriends());
        Assert.Contains(target.Services.Toasts.Items, t => t.Text.StartsWith("Импорт: 1 переименований, 1 ДЗ, 1 друзей"));
        Assert.Equal(1, changed);

        // cancelled dialog: nothing happens, no toast
        target.Services.FileDialogs = new FakeFileDialogs();
        var before = target.Services.Toasts.Items.Count;
        await targetVm.ImportCommand.ExecuteAsync(null);
        Assert.Equal(before, target.Services.Toasts.Items.Count);
    }

    [AvaloniaFact]
    public async Task Qr_Is_Rendered_Into_The_Data_Folder()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);

        await vm.ToggleQrCommand.ExecuteAsync(null);
        Assert.True(vm.QrVisible);
        Assert.NotNull(vm.QrImage);
        Assert.True(File.Exists(Path.Combine(db.Dir, "sync-qr.png")));
        // The seeded export is 1654 chars — past Core's 1500-char limit, so the QR points at the LAN server.
        Assert.Equal("Данных много: QR ведёт на сервер в локальной сети — включите его ниже", vm.QrHint);
        await vm.ToggleQrCommand.ExecuteAsync(null);
        Assert.False(vm.QrVisible);
        Assert.Null(vm.QrImage);

        // A small export (711 chars) fits into the QR itself and gets the plain "scan it in Android" hint.
        using var bare = TestDb.Create(seedPersonalization: false);
        var bareVm = new SettingsViewModel(bare.Services, new ShellViewModel(bare.Services), () => Sun6);
        await bareVm.ToggleQrCommand.ExecuteAsync(null);
        Assert.True(bareVm.QrVisible);
        Assert.Equal("Отсканируйте в Android: Настройки → Синхронизация", bareVm.QrHint);
    }

    /// <summary>The card itself: the QR bitmap has to render on the white backing in both themes.</summary>
    [AvaloniaFact]
    public async Task Qr_Card_Renders_On_Its_White_Backing()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Sun6));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.NavigateTo(SectionKey.Settings);
        var vm = Assert.IsType<SettingsViewModel>(shell.Current);
        await Waits.Until(() => vm.GroupName == "А863С");

        await vm.ToggleQrCommand.ExecuteAsync(null);
        Pump();
        window.MouseWheel(new Point(640, 500), new Vector(0, -12)); // the sync card sits below the fold
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "settings-qr-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "settings-qr-light");
        AssertNoBindingErrors();
    }

    [Fact]
    public async Task Lan_Server_Serves_Export_And_Accepts_Import_Under_The_Gate()
    {
        using var db = TestDb.Create();
        using var server = Loopback(db.Services);
        var imported = 0;
        server.Imported += () => Interlocked.Increment(ref imported);
        server.Start();
        var port = server.Port;
        Assert.True(server.IsRunning);
        Assert.True(port > 0); // 0 asked the OS for a free one
        Assert.Equal("", server.Address); // resolved lazily off the UI thread, never from the property
        Assert.Equal($"http://localhost:{port}/sync/", await server.ResolveAddressAsync());
        Assert.Equal($"http://localhost:{port}/sync/", server.Address); // and cached from then on

        using var http = new HttpClient();
        var json = await http.GetStringAsync($"http://127.0.0.1:{port}/sync/", TestContext.Current.CancellationToken);
        var served = JsonSerializer.Deserialize<SyncService.SyncPayload>(json);
        Assert.NotNull(served);
        Assert.Equal("Матан", Assert.Single(served.Overrides).DisplayName);
        // No HTTP.SYS host matching any more: the numeric loopback address and the path without the trailing slash work too.
        Assert.Contains("\"Version\"", await http.GetStringAsync($"http://127.0.0.1:{port}/sync", TestContext.Current.CancellationToken));

        var stored = Assert.Single(db.Services.Db.GetOverrides());
        var payload = new SyncService.SyncPayload
        {
            ExportedAt = DateTime.UtcNow,
            Overrides = { new Override { SubjectRawNormalized = ParityService.NormalizeSubject(TestDb.MathSubject), Scope = "global", DisplayName = "Математика", CreatedAt = stored.CreatedAt.AddDays(1) } }
        };
        var resp = await http.PostAsync($"http://127.0.0.1:{port}/sync/", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await Waits.Until(() => imported == 1, "Imported event");
        Assert.Equal("Математика", db.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1));
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);

        server.Stop();
        Assert.False(server.IsRunning);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync($"http://127.0.0.1:{port}/sync/", TestContext.Current.CancellationToken));
    }

    /// <summary>A phone pushes over the LAN before the Settings section was ever opened this session: the shell
    /// itself is the subscriber, so the sections recompose and the sidebar badge is refreshed without a
    /// SettingsViewModel existing anywhere. Headless (not a plain Fact) because the server raises Imported on a
    /// pool thread and the shell marshals it with Dispatcher.UIThread.Post — that needs a dispatcher that runs.</summary>
    [AvaloniaFact]
    public async Task Lan_Import_Refreshes_The_Shell_Without_Settings()
    {
        using var source = TestDb.Create();                              // 1 override, 1 homework, 1 friend
        using var target = TestDb.Create(seedPersonalization: false);    // nothing personal yet
        target.Services.LanSync = Loopback(target.Services);             // installed before the shell subscribes
        var shell = new ShellViewModel(target.Services) { Clock = () => Sun6 };
        var changed = 0;
        shell.ScheduleChanged += () => changed++;
        var badge = shell.ToolSections.Single(s => s.Key == SectionKey.Homework);
        await shell.UpdateHomeworkBadgeAsync();
        Assert.Null(badge.Badge);

        target.Services.LanSync.Start();
        using var http = new HttpClient();
        var body = new StringContent(source.Services.Sync.ExportToJson(), Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"http://127.0.0.1:{target.Services.LanSync.Port}/sync/", body, TestContext.Current.CancellationToken);
        Assert.True(resp.IsSuccessStatusCode);

        // The fixture homework («лек ВЫСШ. МАТЕМАТ», created Sat 05.09) is due Mon 07.09 for group 3313 — one day
        // after the pinned Sunday clock, so the badge the shell refreshes after the import reads "1".
        await Waits.Until(() => changed > 0 && badge.Badge == "1", "shell refreshed after the LAN import");
        Assert.Equal("Матан", target.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1));
        target.Services.LanSync.Stop();
    }

    /// <summary>Every refusal the hardening owes a peer on the LAN — and the listener survives all of them.</summary>
    [Fact]
    public async Task Lan_Server_Refuses_Bad_Requests_And_Keeps_Serving()
    {
        using var db = TestDb.Create();
        using var server = Loopback(db.Services);
        var imported = 0;
        server.Imported += () => Interlocked.Increment(ref imported);
        server.Start();
        var url = $"http://127.0.0.1:{server.Port}/sync/";
        using var http = new HttpClient();

        // Declared past the cap: refused before the body is read.
        var oversized = new ByteArrayContent(new byte[LanSyncServer.MaxBodyBytes + 1]);
        oversized.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var big = await http.PostAsync(url, oversized, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Equal("{\"status\":\"error\"}", await big.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Chunked: no declared length, so no bound to check against — refused as well.
        var chunked = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        chunked.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.LengthRequired, (await http.SendAsync(chunked, TestContext.Current.CancellationToken)).StatusCode);

        // Malformed JSON inside the cap.
        var bad = await http.PostAsync(url, new StringContent("{ not json", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Wrong path, wrong method.
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{server.Port}/other", TestContext.Current.CancellationToken)).StatusCode);
        var put = await http.PutAsync(url, new StringContent("{}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(new[] { "GET", "POST" }, put.Content.Headers.Allow.OrderBy(a => a));

        Assert.Equal(0, imported);
        var json = await http.GetStringAsync(url, TestContext.Current.CancellationToken); // still alive after every refusal
        Assert.Equal("Матан", Assert.Single(JsonSerializer.Deserialize<SyncService.SyncPayload>(json)!.Overrides).DisplayName);
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);
    }

    [Fact]
    public async Task A_Stalled_Connection_Does_Not_Block_Other_Clients()
    {
        using var db = TestDb.Create();
        using var server = Loopback(db.Services);
        server.Start();
        using var stalled = new TcpClient();
        await stalled.ConnectAsync(IPAddress.Loopback, server.Port, TestContext.Current.CancellationToken);
        await stalled.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /sync/ HTTP/1.1\r\nHost: x"), TestContext.Current.CancellationToken); // head never completes

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var json = await http.GetStringAsync($"http://127.0.0.1:{server.Port}/sync/", TestContext.Current.CancellationToken); // served while the other handler waits
        Assert.Contains("\"Version\"", json);
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);
    }

    [Fact]
    public async Task Connections_Beyond_The_Cap_Wait_For_A_Slot()
    {
        using var db = TestDb.Create();
        using var server = Loopback(db.Services);
        server.Start();
        var stalled = new List<TcpClient>();
        try
        {
            for (var i = 0; i < LanSyncServer.MaxConnections; i++)
            {
                var c = new TcpClient();
                await c.ConnectAsync(IPAddress.Loopback, server.Port, TestContext.Current.CancellationToken);
                await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST /sync/ HTTP/1.1\r\nContent-Length: 2097152\r\n\r\n"), TestContext.Current.CancellationToken); // declared body never arrives
                stalled.Add(c);
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            await Assert.ThrowsAnyAsync<Exception>(() => http.GetStringAsync($"http://127.0.0.1:{server.Port}/sync/", TestContext.Current.CancellationToken)); // every slot is taken: this one waits in the backlog past its own timeout

            stalled[0].Dispose(); // one slot frees up (the handler's read fails at once)
            using var http2 = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            Assert.Contains("\"Version\"", await http2.GetStringAsync($"http://127.0.0.1:{server.Port}/sync/", TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (var c in stalled) c.Dispose();
        }
    }

    [Fact]
    public async Task Import_Is_Committed_And_Announced_When_The_Client_Vanishes()
    {
        using var db = TestDb.Create();
        using var server = Loopback(db.Services);
        var imported = 0;
        server.Imported += () => Interlocked.Increment(ref imported);
        server.Start();
        var stored = Assert.Single(db.Services.Db.GetOverrides());
        var payload = new SyncService.SyncPayload
        {
            ExportedAt = DateTime.UtcNow,
            Overrides = { new Override { SubjectRawNormalized = ParityService.NormalizeSubject(TestDb.MathSubject), Scope = "global", DisplayName = "Математика", CreatedAt = stored.CreatedAt.AddDays(1) } }
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        await db.Services.CoreGate.WaitAsync(TestContext.Current.CancellationToken); // hold the gate: the import cannot run until we let go
        try
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, server.Port, TestContext.Current.CancellationToken);
                var stream = client.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"POST /sync/ HTTP/1.1\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"), TestContext.Current.CancellationToken);
                await stream.WriteAsync(body, TestContext.Current.CancellationToken);
                await stream.FlushAsync(TestContext.Current.CancellationToken);
                await Waits.Until(() => db.Services.CoreGate.CurrentCount == 0 && server.PendingImports == 1, "the handler is queued on the gate");
            } // the phone drops off the Wi-Fi before any answer can be written
        }
        finally
        {
            db.Services.CoreGate.Release();
        }

        await Waits.Until(() => imported == 1, "Imported after the client vanished");
        Assert.Equal("Математика", db.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1));
        await Waits.Until(() => db.Services.CoreGate.CurrentCount == 1, "gate released");
        await Waits.Until(() => File.ReadAllText(db.Services.Log.CurrentFile).Contains("connection dropped"), "dropped-connection log line"); // a write failure, not an import failure
        Assert.DoesNotContain("ERROR lan sync import", File.ReadAllText(db.Services.Log.CurrentFile));
    }

    [Fact]
    public async Task Target_And_Header_Rules_Follow_The_Rfc()
    {
        using var db = TestDb.Create();
        using var server = Loopback(db.Services);
        server.Start();
        var ct = TestContext.Current.CancellationToken;
        Assert.StartsWith("HTTP/1.1 200", await RawStatusAsync(server.Port, $"GET http://127.0.0.1:{server.Port}/sync/ HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", ct)); // absolute-form
        Assert.StartsWith("HTTP/1.1 200", await RawStatusAsync(server.Port, "GET /sync?x=1 HTTP/1.1\r\nHost: x\r\n\r\n", ct));                                   // query on the slash-less path
        Assert.StartsWith("HTTP/1.1 400", await RawStatusAsync(server.Port, "POST /sync/ HTTP/1.1\r\nContent-Length: 2\r\nContent-Length: 999\r\n\r\n{}", ct));   // conflicting lengths
        Assert.StartsWith("HTTP/1.1 400", await RawStatusAsync(server.Port, "POST /sync/ HTTP/1.1\r\nContent-Length: -5\r\n\r\n", ct));                          // not a length
        Assert.StartsWith("HTTP/1.1 400", await RawStatusAsync(server.Port, "GET sync HTTP/1.1\r\n\r\n", ct));                                                   // no leading slash
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);
    }

    [Fact]
    public void Start_Failure_Leaves_Nothing_Listening_And_Names_The_Busy_Port()
    {
        using var db = TestDb.Create();
        using var squatter = Loopback(db.Services);
        squatter.Start();
        var port = squatter.Port;

        using var server = Loopback(db.Services, port);
        var ex = Assert.Throws<SocketException>(server.Start);
        Assert.Equal(SocketError.AddressAlreadyInUse, ex.SocketErrorCode);
        Assert.False(server.IsRunning);
        Assert.Equal($"Порт {port} занят другой программой", server.StartFailureText(ex));
        Assert.Equal("Не удалось запустить сервер: boom", server.StartFailureText(new InvalidOperationException("boom")));

        squatter.Stop();
        server.Start(); // the port is free now: the same instance can start
        Assert.True(server.IsRunning);
        Assert.Equal(port, server.Port);
    }

    [Fact]
    public async Task Lan_Switch_Persists_And_Reports_Failures()
    {
        using var db = TestDb.Create();
        db.Services.LanSync = Loopback(db.Services); // never the production server (every interface on 8765)
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.False(vm.LanSync);
        Assert.Equal("", vm.LanAddress);

        vm.LanSync = true;
        Assert.True(db.Services.LanSync.IsRunning);
        Assert.True(UiPrefs.Load(db.Services.Prefs.FilePath).LanSync);
        await Waits.Until(() => vm.LanAddress.Length > 0, "LAN address"); // arrives from the resolver, not from the setter
        Assert.Equal($"Адрес: http://localhost:{db.Services.LanSync.Port}/sync/", vm.LanAddress);

        vm.LanSync = false;
        Assert.False(db.Services.LanSync.IsRunning);
        Assert.Equal("", vm.LanAddress);
        Assert.False(UiPrefs.Load(db.Services.Prefs.FilePath).LanSync);

        // The failure branch: a listener already owns the port, so Start() cannot bind it.
        using var squatter = Loopback(db.Services);
        squatter.Start();
        db.Services.LanSync = Loopback(db.Services, squatter.Port);
        var before = db.Services.Toasts.Items.Count;
        vm.LanSync = true;
        Assert.False(vm.LanSync);
        Assert.False(db.Services.LanSync.IsRunning);
        Assert.False(UiPrefs.Load(db.Services.Prefs.FilePath).LanSync);
        Assert.Equal(before + 1, db.Services.Toasts.Items.Count);
        Assert.Equal($"Порт {squatter.Port} занят другой программой", db.Services.Toasts.Items[0].Text);
    }

    /// <summary>R50: an import may adopt MyGroupId (receiver had none) and overwrite ParityInvert (payload newer
    /// than the receiver's LastSyncAt). The sidebar card reads both, so NotifyImportedAsync must refresh it —
    /// before this fix the card kept «Группа не выбрана» until the next timetable refresh.</summary>
    [Fact]
    public async Task Import_Adopts_The_Group_And_Refreshes_The_Sidebar_Card()
    {
        using var source = TestDb.Create();
        var ss = source.Services.Db.GetSettings();
        ss.ParityInvert = true;
        source.Services.Db.SaveSettings(ss);
        var json = source.Services.Sync.ExportToJson();

        using var target = TestDb.Create(seedPersonalization: false);
        var ts = target.Services.Db.GetSettings();
        ts.MyGroupId = "";
        target.Services.Db.SaveSettings(ts);
        var shell = new ShellViewModel(target.Services);
        Assert.Equal("Группа не выбрана", shell.GroupName);

        target.Services.Sync.ImportFromJson(json);
        await shell.NotifyImportedAsync();

        Assert.Equal("А863С", shell.GroupName);
        Assert.True(target.Services.Db.GetSettings().ParityInvert);
        // The card's parity text is computed against the real clock; compute the expectation the same way.
        var expectedOdd = ParityService.IsOddWeek(DateTime.Today, new DateTime(2026, 9, 1), 2, invert: true);
        Assert.StartsWith(target.Services.I18n.FormatParity(expectedOdd), shell.GroupSubtitle);
    }
}
