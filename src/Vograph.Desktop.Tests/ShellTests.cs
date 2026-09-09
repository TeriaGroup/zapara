using System.Net;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Xunit;
using Zapara.Contracts.Sync;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public class ShellTests : UiTest
{
    private static (TestDb Db, ShellViewModel Shell) Make()
    {
        var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        return (db, new ShellViewModel(db.Services));
    }

    // Mirrors ViewModelBaseTests.Probe: the smallest ViewModelBase that exposes RunAsync for a gated call.
    private sealed class Probe(AppServices app) : ViewModelBase(app)
    {
        public Task<string?> Run(Func<string> f) => RunAsync(f, "probe");
    }

    private sealed class Detachable(AppServices app) : ViewModelBase(app)
    {
        public int Detached;
        public override void Detach() => Detached++;
    }

    [AvaloniaFact]
    public void Every_Section_Resolves_To_Its_Real_ViewModel()
    {
        var (db, shell) = Make();
        using (db)
        {
            Assert.Equal(SectionKey.Schedule, shell.CurrentKey);
            Assert.IsType<Features.Week.WeekViewModel>(shell.Section<ViewModelBase>(SectionKey.Week));
            Assert.IsType<Features.Summary.SummaryViewModel>(shell.Section<ViewModelBase>(SectionKey.Summary));
            Assert.IsType<Features.Teachers.TeachersViewModel>(shell.Section<ViewModelBase>(SectionKey.Teachers));
            Assert.IsType<Features.Maps.MapsViewModel>(shell.Section<ViewModelBase>(SectionKey.Maps));
            Assert.IsType<Features.Friends.FriendsViewModel>(shell.Section<ViewModelBase>(SectionKey.Friends));
            Assert.IsType<Features.Homeworks.HomeworkViewModel>(shell.Section<ViewModelBase>(SectionKey.Homework));
            Assert.IsType<CommunitiesViewModel>(shell.Section<ViewModelBase>(SectionKey.Community));
            Assert.IsType<Features.Preferences.SettingsViewModel>(shell.Section<ViewModelBase>(SectionKey.Settings));

            shell.NavigateCommand.Execute("Week");
            Assert.Equal(SectionKey.Week, shell.CurrentKey);
            Assert.Same(shell.Current, shell.Section<ViewModelBase>(SectionKey.Week)); // cached instance
            Assert.True(shell.MainSections[1].IsActive);
            Assert.False(shell.MainSections[0].IsActive);
        }
    }

    [AvaloniaFact]
    public void Group_Card_Shows_My_Group()
    {
        var (db, shell) = Make();
        using (db)
        {
            Assert.Equal("А863С", shell.GroupName);
            Assert.Contains("неделя", shell.GroupSubtitle);
        }
    }

    [AvaloniaFact]
    public async Task Window_Renders_Both_Themes_Hotkeys_Navigate_And_Collapse_Persists()
    {
        var (db, shell) = Make();
        using (db)
        {
            await shell.StartAsync(allowNetwork: false); // the frames show a composed day, not an empty control
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.Focus();

            SetTheme(ThemeVariant.Dark, db.Services.Theme);
            Assert.True(shell.IsDark); // the footer glyph is bound to this: Sun in the dark, not a stale Moon
            Frames.Capture(window, "shell-dark");
            SetTheme(ThemeVariant.Light, db.Services.Theme);
            Assert.False(shell.IsDark);
            Frames.Capture(window, "shell-light");

            window.KeyPress(Key.D9, RawInputModifiers.Control, PhysicalKey.Digit9, null);
            Assert.Equal(SectionKey.Community, shell.CurrentKey);
            var communities = Assert.IsType<CommunitiesViewModel>(shell.Current);
            await Waits.Until(() => communities.NeedAccount, "guest communities");
            Pump();
            Assert.Contains(window.GetVisualDescendants().OfType<EmptyState>(),
                e => e.IsVisible && e.Title == "Чтобы вступить в сообщество, войдите в аккаунт");

            window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, null);
            Assert.Equal(SectionKey.Summary, shell.CurrentKey);

            window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, null);
            var schedule = Assert.IsType<ScheduleViewModel>(shell.Current);
            var before = schedule.DayOffset;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(before + 1, schedule.DayOffset);
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(0, schedule.DayOffset);

            // With the group picker open its search box owns the arrow keys.
            var picker = shell.OpenGroupPickerCommand.ExecuteAsync(null);
            await Waits.Until(() => shell.Dialogs.Current is not null, "group picker dialog");
            Pump();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(0, schedule.DayOffset);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await picker;

            window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
            Assert.True(shell.SidebarCollapsed);
            Assert.True(shell.MainSections[0].IsCompact);
            Assert.True(UiPrefs.Load(db.Services.Prefs.FilePath).SidebarCollapsed);
            Pump(); // let the 232→64 width transition and the section cross-fade finish before the frame
            Frames.Capture(window, "shell-collapsed-light");

            AssertNoBindingErrors();
        }
    }

    [AvaloniaFact]
    public void Theme_Toggle_Flips_Actual_Variant()
    {
        var (db, shell) = Make();
        using (db)
        {
            shell.ToggleThemeCommand.Execute(null);
            var first = Application.Current!.ActualThemeVariant;
            shell.ToggleThemeCommand.Execute(null);
            Assert.NotEqual(first, Application.Current.ActualThemeVariant);
        }
    }

    [AvaloniaFact]
    public void Stored_English_Still_Keeps_Russian_Section_Labels()
    {
        var (db, shell) = Make();
        using (db)
        {
            db.Services.Loc.SetLanguage("en");
            Assert.Equal("ru", db.Services.Loc.Language);
            Assert.Equal("Расписание", shell.MainSections[0].Label);
            Assert.Equal("Сообщества", shell.ToolSections.Single(s => s.Key == SectionKey.Community).Label);
        }
    }

    [AvaloniaFact]
    public async Task Guest_Opens_Community_Section_And_Sees_Need_Account()
    {
        var (db, shell) = Make();
        using (db)
        {
            var nav = Assert.Single(shell.ToolSections, s => s.Key == SectionKey.Community);
            Assert.Equal("navCommunity", nav.LabelKey);
            Assert.Equal("Icon.Community", nav.IconKey);
            Assert.Equal("Ctrl+9", nav.Hotkey);
            Assert.Equal("Nav.Community", nav.AutomationId);
            Assert.Equal("Сообщества", nav.Label);
            Assert.True(Application.Current!.TryFindResource("Icon.Community", out _));

            shell.NavigateCommand.Execute("Community");
            var vm = Assert.IsType<CommunitiesViewModel>(shell.Current);
            await vm.ActivateAsync();
            Assert.Equal(SectionKey.Community, shell.CurrentKey);
            Assert.True(nav.IsActive);
            Assert.True(vm.NeedAccount);
            Assert.Equal("Чтобы вступить в сообщество, войдите в аккаунт", vm.Status);
            Assert.Empty(vm.Communities);
        }
    }

    [AvaloniaFact]
    public async Task Private_Sync_Conflict_Shows_Keep_Local_And_Keep_Server()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        app.Theme = ThemeService.ForApplication(Application.Current!, app.Prefs);
        var shell = new ShellViewModel(app);
        try
        {
            var window = new MainWindow { DataContext = shell };
            window.Show();
            app.Homework.AddHomework("лек ИСТОРИЯ", "локальный черновик", 1, new DateTime(2026, 9, 5, 12, 0, 0));
            var pending = Assert.Single(app.Outbox.Pending());
            var epoch = Guid.Parse("0e0e0e0e-0e0e-4e0e-8e0e-0e0e0e0e0e0e");
            var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
            using var http = new HttpClient(new FakeHttpHandler
            {
                Respond = request =>
                {
                    if (request.Method == HttpMethod.Get)
                        return SyncBody(new SyncMetadata(epoch, 9, 0));
                    var server = new HomeworkValue("лек история", "лек история", "серверная версия", 1, now, null);
                    var record = new SyncRecord("homework", pending.EntityId, 4, false, now, server);
                    return SyncBody(new SyncMutationResult(409, "revision_conflict", new SyncMetadata(epoch, 9, 0), record),
                        HttpStatusCode.Conflict);
                }
            });
            using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/outbox-test/"));
            app.PrivateSync!.Attach(client, _ => Task.FromResult(Token("za_")), background: false);
            await app.PrivateSync.PushPendingAsync(TestContext.Current.CancellationToken);

            var dlg = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Assert.False(dlg.IsExpired);
            Assert.True(dlg.CanChooseVersion);
            Assert.True(dlg.KeepLocalCommand.CanExecute(null));
            Assert.True(dlg.KeepServerCommand.CanExecute(null));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                b => AutomationProperties.GetAutomationId(b) == "Dialog.KeepLocal" && b.IsVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                b => AutomationProperties.GetAutomationId(b) == "Dialog.KeepServer" && b.IsVisible);
            dlg.KeepLocalCommand.Execute(null);
            Assert.Equal(SyncConflictKind.KeepLocal, dlg.Decision!.Kind);
            await Waits.Until(() => !shell.Dialogs.HasDialog, "conflict dialog closed");
            AssertNoBindingErrors();
        }
        finally { shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Expired_Conflict_Shows_Expired_Dialog()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var shell = new ShellViewModel(app);
        try
        {
            var conflict = new PrivateSyncConflict("homework",
                Guid.Parse("11111111-1111-4111-8111-111111111111"), "Снимок синхронизации устарел.");
            var showing = shell.ShowSyncConflictAsync(conflict);
            var dlg = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Assert.True(dlg.IsExpired);
            Assert.False(dlg.CanChooseVersion);
            Assert.False(dlg.KeepLocalCommand.CanExecute(null));
            Assert.False(dlg.KeepServerCommand.CanExecute(null));
            dlg.ConfirmCommand.Execute(null);
            await showing;
            Assert.Equal(SyncConflictKind.Expired410, dlg.Decision!.Kind);
            Assert.True(dlg.Decision.AbortQueuedMutation);
        }
        finally { shell.Stop(); }
    }

    private static HttpResponseMessage SyncBody<T>(T value, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    // R37b: an [AvaloniaFact] runs its body on the headless dispatcher thread, so this reproduces the real
    // shutdown path — App.axaml.cs runs `desktop.Exit += (_, _) => services.Dispose();` on the UI thread —
    // in a way ViewModelBaseTests' plain [Fact] cannot. Before the fix, GatedAsync's release after `await
    // run()` was posted back to this same (now UI-thread-blocked) dispatcher and never ran, so Dispose()
    // always paid the full 2 s CoreGate.Wait timeout. After the fix the gate is released on the pool thread
    // that ran the work, so Dispose() unblocks as soon as the ~200 ms probe call finishes.
    [AvaloniaFact]
    public async Task Dispose_On_The_UI_Thread_Does_Not_Deadlock_On_A_Gated_Call()
    {
        using var db = TestDb.Create();
        var vm = new Probe(db.Services);
        var inGate = new ManualResetEventSlim();

        var work = vm.Run(() => { inGate.Set(); Thread.Sleep(200); return "done"; });
        inGate.Wait(TestContext.Current.CancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        db.Services.Dispose(); // synchronous, on the UI thread — must not deadlock into the 2 s gate wait
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 150, 1500); // checked first: a 2 s deadlock is the primary symptom
        Assert.Equal("done", await work);
    }

    [AvaloniaFact]
    public async Task Stop_Halts_The_Hourly_Check_And_Detaches_Sections()
    {
        var (db, shell) = Make();
        using (db)
        {
            await shell.StartAsync(allowNetwork: false); // no network: the check timer is not started by StartAsync
            shell.StartAutoCheck();
            Assert.True(shell.IsAutoCheckRunning);
            var week = shell.Section<Features.Week.WeekViewModel>(SectionKey.Week);
            shell.NavigateTo(SectionKey.Week);
            await Waits.Until(() => week.Days.Count >= 6, "week days");

            shell.Stop();

            Assert.False(shell.IsAutoCheckRunning);
            var resets = 0;
            week.Days.CollectionChanged += (_, _) => resets++; // every reload starts with Days.Clear() → Reset
            shell.RaiseScheduleChanged();
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.Equal(0, resets); // detached: the section no longer listens
        }
    }

    /// <summary>The switch UiVerify sets before it launches the real exe: App reads VOGRAPH_OFFLINE by that exact
    /// name and nothing else, and an unset variable leaves the run online. The last pair is the assignment
    /// itself — App.AllowNetwork is the negation of the switch, which is what every section then consults.</summary>
    [Fact]
    public void Offline_Switch_Reads_Only_The_VOGRAPH_OFFLINE_Variable()
    {
        var asked = new List<string>();
        Assert.True(App.ReadOfflineSwitch(name => { asked.Add(name); return "1"; }));
        Assert.Equal(new[] { "VOGRAPH_OFFLINE" }, asked);
        Assert.False(App.ReadOfflineSwitch(_ => null));

        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        using (var services = AppServices.Create(dir)) // a fresh composition root, not TestDb's own offline default
        {
            Assert.True(services.AllowNetwork);
            services.AllowNetwork = !App.ReadOfflineSwitch(_ => "1");
            Assert.False(services.AllowNetwork);
            services.AllowNetwork = !App.ReadOfflineSwitch(_ => null);
            Assert.True(services.AllowNetwork);
        }
        try { Directory.Delete(dir, recursive: true); } catch (IOException ex) { Console.Error.WriteLine($"temp dir left behind ({dir}): {ex.Message}"); }
    }

    /// <summary>
    /// The first launch — an empty database — was the last path in the app that downloaded while holding the Core
    /// gate, so on the one start where there is nothing to show yet every other Core call (and Dispose, at two
    /// seconds) queued behind the HTTP timeout. The handler answers from inside the request, so the count it
    /// records is the gate's state at the moment of the fetch; the XML it returns still has to land in SQLite.
    /// </summary>
    [AvaloniaFact]
    public async Task First_Launch_Fetches_Outside_The_Gate_And_Writes_Inside_It()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        using (var services = AppServices.Create(dir))
        {
            var settings = services.Db.GetSettings();
            settings.AutoUpdate = false; // keep the silent startup flow out of this test
            services.Db.SaveSettings(settings);
            services.UpdateSource = new FakeUpdateSource();
            var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
            var gateWhileFetching = -1;
            var handler = new FakeHttpHandler
            {
                Respond = _ =>
                {
                    gateWhileFetching = services.CoreGate.CurrentCount;
                    return FakeHttpHandler.Bytes(System.Text.Encoding.UTF8.GetBytes(xml));
                }
            };
            services.Refresher = new ScheduleRefresher(handler);
            var shell = new ShellViewModel(services);
            Assert.Empty(services.Db.GetAllGroups());

            await shell.StartAsync(allowNetwork: true);
            shell.Stop();

            Assert.Equal(1, gateWhileFetching);                 // nobody was parked behind the download
            Assert.Equal(3, services.Db.GetAllGroups().Count);  // …and it still ended up in the database
            Assert.Equal(1, services.CoreGate.CurrentCount);
            Assert.IsType<ScheduleViewModel>(shell.Current);
        }
        try { Directory.Delete(dir, recursive: true); } catch (IOException ex) { Console.Error.WriteLine($"temp dir left behind ({dir}): {ex.Message}"); }
    }

    /// <summary>
    /// T12-R6's gate on F5 and Settings' «Обновить расписание». The switch promises «no timetable refresh»
    /// (App.axaml.cs), and until that gate landed the two manual routes went straight out to the network on an
    /// offline run. The scripted handler is what makes the gate falsifiable: a refresh that got past it leaves
    /// its GET on the handler and toasts «Расписание обновлено» instead of the offline reason.
    /// </summary>
    [AvaloniaFact]
    public async Task Manual_Refresh_Reaches_No_Network_When_The_Run_Is_Offline()
    {
        var (db, shell) = Make();
        using (db)
        {
            var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
            var handler = new FakeHttpHandler { Respond = _ => FakeHttpHandler.Bytes(System.Text.Encoding.UTF8.GetBytes(xml)) };
            db.Services.Refresher = new ScheduleRefresher(handler);
            Assert.False(db.Services.AllowNetwork);
            await shell.StartAsync(allowNetwork: false); // the run declares its policy; a never-started shell has none
            db.Services.Toasts.Items.Clear();

            await shell.RefreshScheduleCommand.ExecuteAsync(null);

            Assert.Empty(handler.Requests);
            Assert.Single(db.Services.Toasts.Items, t => t.Text == "Не удалось обновить расписание: сеть отключена для этого запуска (VOGRAPH_OFFLINE)");
            Assert.Contains("refresh: skipped, network disabled for this run", File.ReadAllText(db.Services.Log.CurrentFile));
        }
    }

    [Fact]
    public void Register_Detaches_The_Cached_Instance_Once_And_Renavigates_When_It_Was_Current()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var probe = new Detachable(db.Services);
        shell.Register(SectionKey.Week, () => probe);
        shell.NavigateTo(SectionKey.Week);
        Assert.Same(probe, shell.Current);

        var replacement = new Detachable(db.Services);
        shell.Register(SectionKey.Week, () => replacement);

        Assert.Equal(1, probe.Detached);
        Assert.Same(replacement, shell.Current); // the host never keeps showing a section that stopped listening
        Assert.Equal(SectionKey.Week, shell.CurrentKey);

        shell.NavigateTo(SectionKey.Summary);
        shell.Register(SectionKey.Week, () => new Detachable(db.Services)); // not current: detached, no navigation
        Assert.Equal(1, replacement.Detached);
        Assert.Equal(SectionKey.Summary, shell.CurrentKey);
    }
}
