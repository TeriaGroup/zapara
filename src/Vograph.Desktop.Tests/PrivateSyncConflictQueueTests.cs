using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Sync;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncConflictQueueTests : UiTest
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
    private static Guid Initialize(AppServices app)
    {
        var epoch = Guid.NewGuid();
        app.Outbox.BeginSnapshot(new(Guid.NewGuid(), epoch, 2, Now, Now.AddMinutes(10), 0));
        app.Outbox.PublishSnapshot();
        return epoch;
    }
    private static void Seed(AppServices app, Guid epoch, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var id = app.Homework.AddHomework("Математика", "Локальное " + i, 1, Now.UtcDateTime);
            var row = app.Outbox.Pending().Single(p => p.LocalRowId == id);
            var remote = new SyncRecord("homework", row.EntityId, i + 1, false, Now,
                new HomeworkValue("Математика", "математика", "Серверное " + i, 1, Now, null));
            app.Outbox.MarkConflict(row, new(409, "revision_conflict", new(epoch, 2, 0), remote));
        }
    }
    private static Button Button(Window window, string id)
    {
        Pump();
        return window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == id);
    }
    private static async Task<Button> Entry(Window window)
    {
        await Waits.Until(() => window.GetVisualDescendants().OfType<Button>().Any(b =>
            AutomationProperties.GetAutomationId(b) == "Sync.ResolveConflicts" && b.IsVisible), "persistent conflict entry");
        return Button(window, "Sync.ResolveConflicts");
    }
    private static void AttachEmpty(AppServices app, Guid epoch)
    {
        var transport = new HttpClient(new FakeHttpHandler { Respond = request => {
            Assert.Contains("/changes", request.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new ByteArrayContent(SyncJson.Serialize(new SyncChangesPage(new(epoch, 2, 0), 2, 2, false, [])))
            };
            response.Content.Headers.ContentType = new("application/json");
            return response;
        }});
        app.PrivateSync!.Attach(new(transport, new Uri("http://127.0.0.1/")), _ => Task.FromResult("za_" + new string('A', 43)));
    }

    [AvaloniaFact]
    public async Task Two_persisted_conflicts_are_resolved_in_sequence_without_cancelling_the_first_modal()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        Seed(app, Initialize(app), 2);
        var shell = new ShellViewModel(app);
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            var entry = await Entry(window);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VOGRAPH_FRAMES_DIR")))
                Frames.Capture(window, "native-conflicts-persistent-two");
            Click(window, entry);
            var first = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Click(window, Button(window, "Dialog.KeepServer"));
            Assert.True(await first.Completion);
            await Waits.Until(() => shell.Dialogs.Current is SyncConflictDialogViewModel next && next.Conflict.EntityId != first.Conflict.EntityId, "second durable conflict");
            var second = Assert.IsType<SyncConflictDialogViewModel>(shell.Dialogs.Current);
            Click(window, Button(window, "Dialog.KeepServer"));
            Assert.True(await second.Completion);
            await Waits.Until(() => app.Outbox.Drafts().Count == 0, "both choices persisted");
            Assert.Empty(app.Outbox.Pending());
            Assert.All(app.Homework.GetAll(), row => Assert.StartsWith("Серверное", row.Text));
        }
        finally { window.Close(); shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Cancel_keeps_draft_and_can_reopen_without_periodic_popup_or_replacing_an_unrelated_dialog()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var epoch = Initialize(app);
        Seed(app, epoch, 1);
        AttachEmpty(app, epoch);
        var shell = new ShellViewModel(app);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            Click(window, await Entry(window));
            var conflict = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Click(window, Button(window, "Dialog.Cancel"));
            Assert.False(await conflict.Completion);
            await Waits.Until(() => !shell.Dialogs.IsOpen, "cancelled conflict closes");
            Assert.Single(app.Outbox.Drafts());
            await app.PrivateSync!.PullAsync(TestContext.Current.CancellationToken);
            Pump();
            Assert.False(shell.Dialogs.IsOpen);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VOGRAPH_FRAMES_DIR")))
                Frames.Capture(window, "native-conflicts-cancelled-still-accessible");
            var unrelated = new ConfirmDialogViewModel("Другое действие", "Не заменять", "Продолжить", false);
            var showing = shell.Dialogs.ShowAsync(unrelated);
            await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
            Pump();
            Assert.Same(unrelated, shell.Dialogs.Current);
            Assert.False(shell.ResolveSyncConflictsCommand.CanExecute(null));
            Assert.False(Button(window, "Sync.ResolveConflicts").IsEffectivelyEnabled);
            unrelated.CancelCommand.Execute(null);
            await showing;
            Click(window, await Entry(window));
            await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Click(window, Button(window, "Dialog.KeepServer"));
            await Waits.Until(() => app.Outbox.Drafts().Count == 0, "reopened choice persisted");
        }
        finally { window.Close(); shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Reopened_initialized_profile_and_empty_delta_rediscover_durable_conflicts()
    {
        using var dir = new ProfileTestDirectory();
        Guid epoch;
        using (var previous = PrivateSyncOutboxTests.OpenAccount(dir.Root)) { epoch = Initialize(previous); Seed(previous, epoch, 1); }
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        AttachEmpty(app, epoch);
        var shell = new ShellViewModel(app);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            await app.PrivateSync!.PullAsync(TestContext.Current.CancellationToken);
            Assert.True(app.Outbox.SnapshotReady);
            Click(window, await Entry(window));
            await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Click(window, Button(window, "Dialog.KeepServer"));
            await Waits.Until(() => app.Outbox.Drafts().Count == 0, "persisted conflict resolved after restart");
        }
        finally { window.Close(); shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Profile_retirement_during_a_queue_prevents_late_choice_and_following_modal()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        Seed(app, Initialize(app), 2);
        var shell = new ShellViewModel(app);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            Click(window, await Entry(window));
            var conflict = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            shell.SuspendProducers();
            conflict.KeepServerCommand.Execute(null);
            Pump();
            Assert.Equal(2, app.Outbox.Drafts().Count);
            Assert.All(app.Homework.GetAll(), row => Assert.StartsWith("Локальное", row.Text));
            Assert.False(shell.Dialogs.IsOpen);
            shell.ResumeProducers();
            Assert.True((await Entry(window)).IsEnabled);
        }
        finally { window.Close(); shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Cancelled_modal_does_not_accept_a_late_queued_choice()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        Seed(app, Initialize(app), 1);
        var shell = new ShellViewModel(app);
        try
        {
            var draft = app.Outbox.Drafts().Single();
            var showing = shell.ShowSyncConflictAsync(new(draft.EntityType, draft.EntityId, "Конфликт"));
            var dialog = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            dialog.CancelCommand.Execute(null);
            dialog.KeepServerCommand.Execute(null);
            await showing;
            Assert.Single(app.Outbox.Drafts());
            Assert.StartsWith("Локальное", app.Homework.GetAll().Single().Text);
        }
        finally { shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Empty_delta_discovers_a_draft_created_after_shell_start_without_opening_a_modal()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var epoch = Initialize(app);
        AttachEmpty(app, epoch);
        var shell = new ShellViewModel(app);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            Pump();
            await Waits.Until(() => app.Work.Outstanding == 0, "startup discovery complete");
            Assert.False(shell.HasSyncConflicts);
            Seed(app, epoch, 1);
            await app.PrivateSync!.PullAsync(TestContext.Current.CancellationToken);
            await Entry(window);
            Assert.Equal(1, shell.SyncConflictCount);
            Assert.False(shell.Dialogs.IsOpen);
        }
        finally { window.Close(); shell.Stop(); }
    }
}

public sealed class PrivateSyncConflictDiscoveryLifetimeTests
{
    [Fact]
    public async Task Initial_discovery_does_not_leave_an_unpumped_UI_lease_blocking_profile_exit()
    {
        await using var harness = new ProfileHarness();
        var login = await harness.Coordinator.LoginAsync("Test.User", AccountClientTestSupport.Password,
            TestContext.Current.CancellationToken);
        Assert.True(login.Committed);
        await harness.Coordinator.ExitAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Coordinator.Current.Services.Work.Outstanding);
    }

    [Fact]
    public async Task Background_discovery_signal_does_not_hold_a_profile_open_when_UI_is_not_pumped()
    {
        await using var harness = new ProfileHarness();
        Assert.True((await harness.Coordinator.LoginAsync("Test.User", AccountClientTestSupport.Password,
            TestContext.Current.CancellationToken)).Committed);
        var app = harness.Coordinator.Current.Services;
        using var transport = new HttpClient(new FakeHttpHandler { Respond = _ => {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) {
                Content = new ByteArrayContent(SyncJson.Serialize(new SyncError(503, "db_unavailable")))
            };
            response.Content.Headers.ContentType = new("application/json");
            return response;
        }});
        app.PrivateSync!.Attach(new(transport, new Uri("http://127.0.0.1/")), _ => Task.FromResult("za_" + new string('A', 43)));
        await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
        await harness.Coordinator.ExitAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(0, app.Work.Outstanding);
    }
}
