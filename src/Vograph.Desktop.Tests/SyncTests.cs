using System.Net;
using System.Net.Http;
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

    private static async Task WaitAsync(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done(), "condition not met in time");
    }

    /// <summary>An ephemeral loopback port: 8765 may well be taken on the machine running the suite.</summary>
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
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
        await WaitAsync(() => vm.GroupName == "А863С");

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
        var port = FreePort();
        using var server = new LanSyncServer(db.Services, port, localhostOnly: true);
        var imported = 0;
        server.Imported += () => Interlocked.Increment(ref imported);
        server.Start();
        Assert.True(server.IsRunning);
        Assert.EndsWith($":{port}/sync/", server.Address);

        // HTTP.SYS matches the "localhost" prefix by host name only: a request to 127.0.0.1 is answered with 400.
        using var http = new HttpClient();
        var json = await http.GetStringAsync($"http://localhost:{port}/sync/", TestContext.Current.CancellationToken);
        var served = JsonSerializer.Deserialize<SyncService.SyncPayload>(json);
        Assert.NotNull(served);
        Assert.Equal("Матан", Assert.Single(served.Overrides).DisplayName);

        // A newer override wins on import (SyncService compares CreatedAt), so the display name really flips.
        // The stamp is taken from the stored row: Core reads it back as a local DateTime, so "UtcNow" would lose.
        var stored = Assert.Single(db.Services.Db.GetOverrides());
        var payload = new SyncService.SyncPayload
        {
            ExportedAt = DateTime.UtcNow,
            Overrides =
            {
                new Override
                {
                    SubjectRawNormalized = ParityService.NormalizeSubject(TestDb.MathSubject),
                    Scope = "global",
                    DisplayName = "Математика",
                    CreatedAt = stored.CreatedAt.AddDays(1)
                }
            }
        };
        var body = JsonSerializer.Serialize(payload);
        var resp = await http.PostAsync($"http://localhost:{port}/sync/", new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.True(resp.IsSuccessStatusCode);
        await WaitAsync(() => imported == 1);
        Assert.Equal("Математика", db.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1));
        Assert.Equal(1, db.Services.CoreGate.CurrentCount);

        server.Stop();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Lan_Switch_Persists_And_Reports_Failures()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.False(vm.LanSync);

        vm.LanSync = true; // the real listener needs URL ACL rights; either it starts or the switch reverts with a toast
        await Task.Delay(100, TestContext.Current.CancellationToken);
        if (db.Services.LanSync.IsRunning)
        {
            Assert.True(UiPrefs.Load(db.Services.Prefs.FilePath).LanSync);
            Assert.StartsWith("Адрес: http://", vm.LanAddress);
            vm.LanSync = false;
            Assert.False(db.Services.LanSync.IsRunning);
        }
        else
        {
            Assert.False(vm.LanSync);
            Assert.Contains(db.Services.Toasts.Items, t => t.Text.StartsWith("Не удалось запустить сервер"));
        }
    }
}
